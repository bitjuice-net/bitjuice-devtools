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

// Manifest repositories: apps/<application>.app for every application, and index.json with their revisions.
// The indexes are kept in cache/indexes.json, `source sync` gets them again.
// A fetched .app records its repository in origin, so the tools directory alone knows where it came from.
public partial class SourceManager(IOptions<Settings> options, IStorage storage, IToolDefinitionProvider definitions, IRepositoryClient client, TimeProvider time = null)
    : ISourceManager
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;
    private static readonly string FileName = PathUtils.GetConfigPath("sources.json");
    private static readonly string IndexesFileName = PathUtils.GetCachePath("indexes.json");

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly Settings settings = options.Value;

    private SourcesState state;
    private SourcesState State => state ??= storage.Load<SourcesState>(FileName);

    private Dictionary<string, SyncedIndex> indexes;
    private Dictionary<string, SyncedIndex> Indexes => indexes ??= new Dictionary<string, SyncedIndex>(
        storage.Load<Dictionary<string, SyncedIndex>>(IndexesFileName).Where(i => i.Value.Index != null), Comparer);

    // A list older than this is downloaded again when it is needed, `source sync` does it right away.
    public static readonly TimeSpan MaxIndexAge = TimeSpan.FromHours(24);

    private readonly TimeProvider time = time ?? TimeProvider.System;

    public void Add(string repository)
    {
        repository = Normalize(repository);

        if (State.Repositories.Contains(repository, Comparer))
        {
            Console.WriteLine($"{repository} is already a source");
            return;
        }

        var index = DownloadIndex(repository);
        State.Repositories.Add(repository);
        Indexes[repository] = Synced(index);
        Save();
        SaveIndexes();

        Console.WriteLine($"Added {repository} with {index.Apps.Count} application(s)");
    }

    public void Remove(string repository)
    {
        repository = Normalize(repository);

        if (State.Repositories.RemoveAll(i => Comparer.Equals(i, repository)) == 0)
            throw new InvalidOperationException($"{repository} is not a source, see `source list`");

        Indexes.Remove(repository);
        Save();
        SaveIndexes();
        Console.WriteLine($"Removed {repository}, manifests fetched from it stay");
    }

    public void List(string repository)
    {
        if (repository != null)
        {
            ListApplications(repository);
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

    private void ListApplications(string repository)
    {
        repository = Normalize(repository);
        var source = State.Repositories.FirstOrDefault(i => Comparer.Equals(i, repository))
            ?? throw new InvalidOperationException($"{repository} is not a source, see `source list`");

        var table = new TextTable("APPLICATION", "REVISION", "LOCAL");
        var root = PathUtils.GetRootedPath(settings.RootPath);

        foreach (var (name, entry) in GetIndex(source).Apps)
        {
            var local = definitions.GetManifest(root, name);
            table.AddRow(name, entry.Revision.ToString(), local != null && Comparer.Equals(local.Origin?.Repository, source) ? local.Revision.ToString() : "");
        }

        table.Write();
    }

    public void Sync()
    {
        if (State.Repositories.Count == 0)
        {
            Console.WriteLine("No sources, add one with `source add owner/repo`");
            return;
        }

        foreach (var repository in State.Repositories)
        {
            try
            {
                var index = DownloadIndex(repository);
                Indexes[repository] = Synced(index);
                Console.WriteLine($"{repository}: {index.Apps.Count} application(s)");
            }
            catch (Exception e) when (IsUnavailable(e))
            {
                Console.Error.WriteLine($"{repository}: {e.Message}{(Indexes.ContainsKey(repository) ? ", kept the previous list" : "")}");
            }
        }

        SaveIndexes();
    }

    public bool Refresh(string application)
    {
        var root = PathUtils.GetRootedPath(settings.RootPath);
        var directory = definitions.FindDirectory(root, application);
        var fileName = directory != null ? Path.Combine(directory, ToolManifest.FileName) : null;
        var local = fileName != null ? storage.Load<ToolManifest>(fileName) : null;

        var located = Locate(application, local?.Origin?.Repository);
        if (located == null)
        {
            if (local?.Install != null)
                return false;

            throw new InvalidOperationException($"{application} has no install section, locally or in sources: {State.Repositories.Join(", ")}. " +
                "`source sync` gets their newest lists");
        }

        var (repository, entry) = located.Value;

        if (local == null)
            return Write(application, repository, entry.Revision, Path.Combine(root, application));

        // A manifest not fetched from a source is at its own revision, 1 when it has none.
        if (entry.Revision <= Math.Max(1, local.Revision))
        {
            if (local.Install == null)
                throw new InvalidOperationException($"`{fileName}` has no install section, `app reset {application}` replaces it with the one from {repository}");

            return false;
        }

        // A hand-made manifest is replaced by a newer one from a source, a fetched one changed by hand stays.
        if (local.Origin != null && ManifestHash.IsModified(fileName))
        {
            Console.WriteLine($"Kept `{fileName}`, it has local changes. `app reset {application}` replaces it with revision {entry.Revision} from {repository}");
            return false;
        }

        return Write(application, repository, entry.Revision, directory);
    }

    public void Reset(string application)
    {
        var root = PathUtils.GetRootedPath(settings.RootPath);
        var directory = definitions.FindDirectory(root, application);
        var origin = directory != null ? storage.Load<ToolManifest>(Path.Combine(directory, ToolManifest.FileName)).Origin?.Repository : null;

        var (repository, entry) = Locate(application, origin)
            ?? throw new InvalidOperationException($"No source has {application}: {State.Repositories.Join(", ")}. `source sync` gets their newest lists");

        if (!Write(application, repository, entry.Revision, directory ?? Path.Combine(root, application)))
            Console.WriteLine($"{application}: the manifest is already the one from {repository}");
    }

    private static void Validate(ToolManifest manifest, string name, string location)
    {
        if (manifest.Name != name)
            throw new InvalidOperationException($"{ToolManifest.FileName} in `{location}` is named {manifest.Name}, expected {name}");

        if (!manifest.Versioned)
            throw new InvalidOperationException($"{ToolManifest.FileName} in `{location}` needs \"versioned\": true to install versions");

        if (manifest.Revision < 1)
            throw new InvalidOperationException($"{ToolManifest.FileName} in `{location}` needs a revision of 1 or more");

        if (manifest.Install == null)
            throw new InvalidOperationException($"{ToolManifest.FileName} in `{location}` has no install section");
    }

    // The repository the application was fetched from keeps it. Otherwise the source with the newest revision wins,
    // the first one of them when they are equal.
    private (string Repository, IndexEntry Entry)? Locate(string application, string fetched)
    {
        (string Repository, IndexEntry Entry)? newest = null;

        foreach (var repository in State.Repositories)
        {
            var index = TryGetIndex(repository, out var error);
            if (index == null)
            {
                Console.Error.WriteLine($"Skipped {repository}: {error}");
                continue;
            }

            if (!index.Apps.TryGetValue(application, out var entry))
                continue;

            if (Comparer.Equals(repository, fetched))
                return (repository, entry);

            if (newest == null || entry.Revision > newest.Value.Entry.Revision)
                newest = (repository, entry);
        }

        return newest;
    }

    // Writes .app as the repository has it, with origin added, through a temporary file so it is never half written.
    // Returns false when the file already has that content.
    private bool Write(string application, string repository, int revision, string directory)
    {
        var path = ManifestPath(application);
        var content = Download(repository, path)
            ?? throw new InvalidOperationException($"{repository} lists {application} but has no `{path}`, `source sync` gets its newest list");

        var location = $"{repository}/{path}";
        var manifest = JsonStorage.Parse<ToolManifest>(content);
        Validate(manifest, application, location);

        if (manifest.Revision != revision)
            Console.Error.WriteLine($"{location}: {RepositoryIndex.FileName} lists revision {revision}, {ToolManifest.FileName} has {manifest.Revision}");

        var json = JsonNode.Parse(content) as JsonObject
            ?? throw new InvalidOperationException($"{ToolManifest.FileName} in `{location}` is not a JSON object");
        json.Remove(ManifestHash.OriginProperty);
        json[ManifestHash.OriginProperty] = new JsonObject
        {
            ["repository"] = repository,
            ["sha256"] = ManifestHash.Compute(json)
        };

        var fileName = Path.Combine(directory, ToolManifest.FileName);
        var text = json.ToJsonString(Indented);
        if (File.Exists(fileName) && File.ReadAllText(fileName) == text)
            return false;

        Directory.CreateDirectory(directory);
        var temporary = fileName + ".tmp";
        File.WriteAllText(temporary, text);
        File.Move(temporary, fileName, overwrite: true);

        Console.WriteLine($"{application}: manifest at revision {manifest.Revision} from {repository}");
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

    // The synced index. One never synced or older than a day is downloaded, an unavailable repository keeps the old one.
    private RepositoryIndex GetIndex(string repository)
    {
        var cached = Indexes.GetValueOrDefault(repository);
        if (cached != null && time.GetUtcNow() - cached.Synced < MaxIndexAge)
            return cached.Index;

        try
        {
            var index = DownloadIndex(repository);
            Indexes[repository] = Synced(index);
            SaveIndexes();
            return index;
        }
        catch (Exception e) when (cached != null && IsUnavailable(e))
        {
            Console.Error.WriteLine($"{repository}: {e.Message}, using the list from {cached.Synced.LocalDateTime:g}");
            return cached.Index;
        }
    }

    private SyncedIndex Synced(RepositoryIndex index)
    {
        return new SyncedIndex { Synced = time.GetUtcNow(), Index = index };
    }

    private RepositoryIndex DownloadIndex(string repository)
    {
        var content = Download(repository, RepositoryIndex.FileName)
            ?? throw new InvalidOperationException($"{repository} has no {RepositoryIndex.FileName}, is it a manifest repository?");

        return JsonStorage.Parse<RepositoryIndex>(content);
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
        storage.Save(FileName, State, changesState: false);
    }

    private void SaveIndexes()
    {
        storage.Save(IndexesFileName, Indexes, changesState: false);
    }

    [GeneratedRegex(@"^[\w.-]+/[\w.-]+$")]
    private static partial Regex GitHubRepository();
}
