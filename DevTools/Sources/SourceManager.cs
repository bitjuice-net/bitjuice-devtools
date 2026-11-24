using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DevTools.Catalog;
using DevTools.Catalog.Models;
using DevTools.Infrastructure;
using DevTools.Sources.Models;
using Microsoft.Extensions.Options;

namespace DevTools.Sources;

// Manifest repositories: apps/<application>.app for every application and index.json with their names.
// Nothing of them is cached, every command that needs a repository reads it again.
// A fetched .app records its repository and hash in origin, so the tools directory alone knows where it came from
// and whether it was changed by hand.
public partial class SourceManager(IOptions<Settings> options, IStorage storage, IToolDefinitionProvider definitions, IRepositoryClient client)
    : ISourceManager
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;
    private static readonly string FileName = PathUtils.GetConfigPath("sources.json");

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly Settings settings = options.Value;

    private SourcesState state;
    private SourcesState State => state ??= storage.Load<SourcesState>(FileName);

    // Read once per command.
    private readonly Dictionary<string, RepositoryIndex> indexes = new(Comparer);

    public void Add(string repository)
    {
        repository = Normalize(repository);

        if (State.Repositories.Contains(repository, Comparer))
        {
            Console.WriteLine($"{repository} is already a source");
            return;
        }

        var index = GetIndex(repository);
        State.Repositories.Add(repository);
        Save();

        Console.WriteLine($"Added {repository} with {index.Apps.Count} application(s)");
    }

    public void Remove(string repository)
    {
        repository = Normalize(repository);

        if (State.Repositories.RemoveAll(i => Comparer.Equals(i, repository)) == 0)
            throw new InvalidOperationException($"{repository} is not a source, see `source list`");

        Save();
        Console.WriteLine($"Removed {repository}, manifests fetched from it stay");
    }

    public void List(string repository)
    {
        if (repository != null)
        {
            ListApplications(FindSource(repository));
            return;
        }

        if (State.Repositories.Count == 0)
        {
            Console.WriteLine("No sources, add one with `source add owner/repo`");
            return;
        }

        var table = new TextTable("REPOSITORY", "APPLICATIONS");

        foreach (var source in State.Repositories)
        {
            var index = TryGetIndex(source, out var error);
            table.AddRow(source, index != null ? index.Apps.Count.ToString() : $"unavailable: {error}");
        }

        table.Write();
    }

    private void ListApplications(string source)
    {
        var table = new TextTable("APPLICATION", "LOCAL");
        var root = PathUtils.GetRootedPath(settings.RootPath);

        foreach (var name in GetIndex(source).Apps.Order(StringComparer.Ordinal))
            table.AddRow(name, DescribeLocal(root, name, source));

        table.Write();
    }

    // How the application in the tools directory relates to the source.
    private string DescribeLocal(string root, string application, string source)
    {
        var directory = definitions.FindDirectory(root, application);
        if (directory == null)
            return "";

        var local = storage.Load<ToolManifest>(Path.Combine(directory, ToolManifest.FileName));
        if (local.Origin == null)
            return $"local {ToolManifest.FileName}";

        if (!Comparer.Equals(local.Origin.Repository, source))
            return $"from {local.Origin.Repository}";

        return ManifestHash.IsModified(Path.Combine(directory, ToolManifest.FileName)) ? "fetched, changed locally" : "fetched";
    }

    public bool Refresh(string application, string source = null)
    {
        var root = PathUtils.GetRootedPath(settings.RootPath);
        var directory = definitions.FindDirectory(root, application);
        var fileName = directory != null ? Path.Combine(directory, ToolManifest.FileName) : null;
        var local = fileName != null ? storage.Load<ToolManifest>(fileName) : null;

        // A hand-made manifest is never replaced on its own, only `app reset` does it.
        if (local != null && local.Origin == null && source == null)
        {
            if (local.Install != null)
                return false;

            var found = Locate(application, null, null);
            throw new InvalidOperationException(found != null
                ? $"`{fileName}` has no install section, `app reset {application}` replaces it with the one from {found}"
                : $"`{fileName}` has no install section and no source has {application}: {State.Repositories.Join(", ")}");
        }

        var repository = Locate(application, local?.Origin?.Repository, source);
        if (repository == null)
        {
            if (local?.Install != null)
            {
                Console.Error.WriteLine($"{local.Origin.Repository} no longer has {application}, kept `{fileName}`. " +
                    $"`app reset {application} --source <repository>` takes it from another source");
                return false;
            }

            throw new InvalidOperationException($"No source has {application}: {State.Repositories.Join(", ")}");
        }

        var fetched = Fetch(application, repository);

        // A fetched manifest changed by hand stays, it is worth a word only when the source has a different one.
        if (local != null && source == null && ManifestHash.IsModified(fileName))
        {
            if (fetched.Hash != local.Origin.Sha256)
                Console.WriteLine($"Kept `{fileName}`, it has local changes. `app reset {application}` replaces it with the newer one from {repository}");

            return false;
        }

        return Write(application, repository, fetched, directory ?? Path.Combine(root, application));
    }

    public void Reset(string application, string source = null)
    {
        var root = PathUtils.GetRootedPath(settings.RootPath);
        var directory = definitions.FindDirectory(root, application);
        var origin = directory != null ? storage.Load<ToolManifest>(Path.Combine(directory, ToolManifest.FileName)).Origin?.Repository : null;

        var repository = Locate(application, origin, source)
            ?? throw new InvalidOperationException(origin != null
                ? $"{origin} no longer has {application}, `app reset {application} --source <repository>` takes it from another source"
                : $"No source has {application}: {State.Repositories.Join(", ")}");

        if (!Write(application, repository, Fetch(application, repository), directory ?? Path.Combine(root, application)))
            Console.WriteLine($"{application}: the manifest is already the one from {repository}");
    }

    private static void Validate(ToolManifest manifest, string name, string location)
    {
        if (manifest.Name != name)
            throw new InvalidOperationException($"{ToolManifest.FileName} in `{location}` is named {manifest.Name}, expected {name}");

        if (manifest.Flat)
            throw new InvalidOperationException($"{ToolManifest.FileName} in `{location}` is flat, only applications with versions can be installed");

        if (manifest.Install == null)
            throw new InvalidOperationException($"{ToolManifest.FileName} in `{location}` has no install section");
    }

    // The repository to take the application from: the one chosen with --source, otherwise the one it was fetched from,
    // otherwise the only source that has it. Null when there is none, more than one is a question for the user.
    private string Locate(string application, string fetched, string source)
    {
        if (source != null)
        {
            var chosen = FindSource(source);
            if (!GetIndex(chosen).Apps.Contains(application))
                throw new InvalidOperationException($"{chosen} has no {application}, `source list {chosen}` lists its applications");

            return chosen;
        }

        if (fetched != null)
        {
            var repository = State.Repositories.FirstOrDefault(i => Comparer.Equals(i, fetched));
            if (repository == null)
                return null;

            return GetIndex(repository).Apps.Contains(application) ? repository : null;
        }

        var found = new List<string>();
        foreach (var repository in State.Repositories)
        {
            var index = TryGetIndex(repository, out var error);
            if (index == null)
                Console.Error.WriteLine($"Skipped {repository}: {error}");
            else if (index.Apps.Contains(application))
                found.Add(repository);
        }

        if (found.Count > 1)
            throw new InvalidOperationException($"{application} is in more than one source: {found.Join(", ")}. Choose one with --source <repository>");

        return found.SingleOrDefault();
    }

    private record FetchedManifest(ToolManifest Manifest, JsonObject Json, string Hash);

    private FetchedManifest Fetch(string application, string repository)
    {
        var path = ManifestPath(application);
        var location = $"{repository}/{path}";
        var content = Download(repository, path)
            ?? throw new InvalidOperationException($"{repository} lists {application} in {RepositoryIndex.FileName} but has no `{path}`");

        var manifest = JsonStorage.Parse<ToolManifest>(content);
        Validate(manifest, application, location);

        var json = JsonNode.Parse(content) as JsonObject
            ?? throw new InvalidOperationException($"{ToolManifest.FileName} in `{location}` is not a JSON object");
        json.Remove(ManifestHash.OriginProperty);

        return new FetchedManifest(manifest, json, ManifestHash.Compute(json));
    }

    // Writes .app as the repository has it, with origin added, through a temporary file so it is never half written.
    // Returns false when the file already has that content.
    private static bool Write(string application, string repository, FetchedManifest fetched, string directory)
    {
        var json = (JsonObject)fetched.Json.DeepClone();
        json[ManifestHash.OriginProperty] = new JsonObject
        {
            ["repository"] = repository,
            ["sha256"] = fetched.Hash
        };

        var fileName = Path.Combine(directory, ToolManifest.FileName);
        var text = json.ToJsonString(Indented);
        if (File.Exists(fileName) && File.ReadAllText(fileName) == text)
            return false;

        Directory.CreateDirectory(directory);
        var temporary = fileName + ".tmp";
        File.WriteAllText(temporary, text);
        File.Move(temporary, fileName, overwrite: true);

        Console.WriteLine($"{application}: manifest from {repository}");
        return true;
    }

    // A repository keeps the manifests in apps/<application>.app, next to index.json.
    public static string ManifestPath(string application)
    {
        return $"apps/{application}{ToolManifest.FileName}";
    }

    private byte[] Download(string repository, string path)
    {
        return client.GetFileAsync(repository, path).GetAwaiter().GetResult();
    }

    private string FindSource(string repository)
    {
        var normalized = Normalize(repository);
        return State.Repositories.FirstOrDefault(i => Comparer.Equals(i, normalized))
            ?? throw new InvalidOperationException($"{repository} is not a source, see `source list`");
    }

    private RepositoryIndex TryGetIndex(string repository, out string error)
    {
        try
        {
            error = null;
            return GetIndex(repository);
        }
        catch (Exception e) when (IsUnavailable(e))
        {
            error = e.Message;
            return null;
        }
    }

    private RepositoryIndex GetIndex(string repository)
    {
        if (indexes.TryGetValue(repository, out var index))
            return index;

        var content = Download(repository, RepositoryIndex.FileName)
            ?? throw new InvalidOperationException($"{repository} has no {RepositoryIndex.FileName}, is it a manifest repository?");

        index = JsonStorage.Parse<RepositoryIndex>(content);
        index.Apps ??= new List<string>();
        return indexes[repository] = index;
    }

    private static bool IsUnavailable(Exception e)
    {
        return e is InvalidOperationException or HttpRequestException or IOException or JsonException;
    }

    private static string Normalize(string repository)
    {
        if (Path.IsPathRooted(repository))
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(repository));

        if (!GitHubRepository().IsMatch(repository))
            throw new InvalidOperationException($"`{repository}` is neither owner/repo on GitHub nor a full directory path");

        return repository;
    }

    private void Save()
    {
        storage.Save(FileName, State);
    }

    [GeneratedRegex(@"^[\w.-]+/[\w.-]+$")]
    private static partial Regex GitHubRepository();
}
