using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ConsoleTables;
using DevTools.Catalog;
using DevTools.Infrastructure;
using DevTools.Installation;
using Microsoft.Extensions.Options;

namespace DevTools.Sources;

public interface ISourceManager
{
    void Add(string repository);
    void Remove(string repository);
    void List();
    void Update(string application, bool force);
    void Index(string directory);

    // Gets .app and .source of an application that has no .source yet, used by `app install`.
    void Fetch(string application);
}

// Manifest repositories: a directory per application with .app and .source, and index.json with their revisions.
public partial class SourceManager(IOptions<Settings> options, IStorage storage, IToolDefinitionProvider definitions, IRepositoryClient client)
    : ISourceManager
{
    private const string ManifestFileName = ".app";
    private static readonly string[] ManifestFiles = { ManifestFileName, ToolSource.FileName };
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;
    private static readonly string FileName = PathUtils.GetDataPath("sources.json");

    private readonly Settings settings = options.Value;
    private readonly Dictionary<string, RepositoryIndex> indexes = new(Comparer);

    private SourcesState state;
    private SourcesState State => state ??= storage.Load<SourcesState>(FileName);

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

    public void List()
    {
        if (State.Repositories.Count == 0)
        {
            Console.WriteLine("No sources, add one with `source add owner/repo`");
            return;
        }

        var table = new ConsoleTable("Repository", "Applications");

        foreach (var repository in State.Repositories)
        {
            var index = TryGetIndex(repository, out var error);
            table.AddRow(repository, index != null ? index.Apps.Keys.Join(", ") : $"unavailable: {error}");
        }

        table.Write(Format.MarkDown);
    }

    public void Fetch(string application)
    {
        var (repository, entry) = Locate(application)
            ?? throw new InvalidOperationException($"No `{ToolSource.FileName}` file found for {application}, locally or in sources: {State.Repositories.Join(", ")}");

        Write(application, repository, entry.Revision, false);
    }

    public void Update(string application, bool force)
    {
        var names = application != null ? new[] { application } : State.Apps.Keys.Order(StringComparer.Ordinal).ToArray();

        if (names.Length == 0)
        {
            Console.WriteLine("No manifests were fetched from sources yet, `app install` fetches them");
            return;
        }

        foreach (var name in names)
        {
            var located = Locate(name);
            if (located == null)
            {
                if (application != null)
                    throw new InvalidOperationException($"{name} is not in sources: {State.Repositories.Join(", ")}");

                Console.WriteLine($"{name}: no longer in sources, kept as it is");
                continue;
            }

            var (repository, entry) = located.Value;
            var fetched = State.Apps.GetValueOrDefault(name);

            if (!force && fetched != null && Comparer.Equals(fetched.Repository, repository) && entry.Revision <= fetched.Revision)
            {
                Console.WriteLine($"{name}: up to date, revision {fetched.Revision}");
                continue;
            }

            Write(name, repository, entry.Revision, force);
        }
    }

    public void Index(string directory)
    {
        directory = Path.GetFullPath(directory);
        var index = new RepositoryIndex();

        foreach (var app in new DirectoryInfo(directory).EnumerateDirectories().Where(i => !i.Name.StartsWith('.')))
        {
            var manifestFile = Path.Combine(app.FullName, ManifestFileName);
            var sourceFile = Path.Combine(app.FullName, ToolSource.FileName);

            if (!File.Exists(manifestFile) && !File.Exists(sourceFile))
                continue;

            if (!File.Exists(manifestFile) || !File.Exists(sourceFile))
                throw new InvalidOperationException($"`{app.FullName}` needs both {ManifestFileName} and {ToolSource.FileName}");

            var source = storage.Load<ToolSource>(sourceFile);
            Validate(storage.Load<ToolManifest>(manifestFile), source, app.Name, app.FullName);
            index.Apps[app.Name] = new IndexEntry { Revision = source.Revision };
        }

        var file = Path.Combine(directory, RepositoryIndex.FileName);
        storage.Save(file, index);
        Console.WriteLine($"Wrote {file} with {index.Apps.Count} application(s)");
    }

    private static void Validate(ToolManifest manifest, ToolSource source, string name, string location)
    {
        if (manifest.Name != name)
            throw new InvalidOperationException($"{ManifestFileName} in `{location}` is named {manifest.Name}, expected {name}");

        if (!manifest.Versioned)
            throw new InvalidOperationException($"{ManifestFileName} in `{location}` needs \"versioned\": true to install versions");

        if (source.Format > ToolSource.SupportedFormat)
            throw new InvalidOperationException($"{ToolSource.FileName} in `{location}` has format {source.Format}, this devtools supports up to {ToolSource.SupportedFormat}, update devtools");

        if (source.Revision < 1)
            throw new InvalidOperationException($"{ToolSource.FileName} in `{location}` needs a revision of 1 or more");
    }

    // The repository the application was fetched from wins, then the first source that has it.
    private (string Repository, IndexEntry Entry)? Locate(string application)
    {
        var fetched = State.Apps.GetValueOrDefault(application)?.Repository;
        var candidates = State.Repositories.OrderBy(i => Comparer.Equals(i, fetched) ? 0 : 1);

        foreach (var repository in candidates)
        {
            var index = TryGetIndex(repository, out var error);
            if (index == null)
                Console.Error.WriteLine($"Skipped {repository}: {error}");
            else if (index.Apps.TryGetValue(application, out var entry))
                return (repository, entry);
        }

        return null;
    }

    // Replaces a local file only when it is still the one fetched before, so changes made by hand stay unless forced.
    private void Write(string application, string repository, int revision, bool force)
    {
        var files = ManifestFiles.ToDictionary(i => i, i => Download(repository, $"{application}/{i}")
            ?? throw new InvalidOperationException($"{repository} lists {application} but has no `{application}/{i}`"));

        Validate(JsonStorage.Parse<ToolManifest>(files[ManifestFileName]), JsonStorage.Parse<ToolSource>(files[ToolSource.FileName]), application, $"{repository}/{application}");

        var root = PathUtils.GetRootedPath(settings.RootPath);
        var directory = definitions.FindDirectory(root, application) ?? Path.Combine(root, application);
        Directory.CreateDirectory(directory);

        var fetched = State.Apps.GetValueOrDefault(application);
        var hashes = new Dictionary<string, string>();
        var kept = new List<string>();

        foreach (var (file, content) in files)
        {
            var path = Path.Combine(directory, file);
            var hash = Hash(content);
            var local = File.Exists(path) ? Hash(File.ReadAllBytes(path)) : null;

            if (local != null && local != hash && !force && local != fetched?.Hashes.GetValueOrDefault(file))
            {
                kept.Add(path);
                if (fetched?.Hashes.GetValueOrDefault(file) is { } previous)
                    hashes[file] = previous;
                continue;
            }

            if (local != hash)
                File.WriteAllBytes(path, content);
            hashes[file] = hash;
        }

        // With a file kept the application is not at the new revision, so the next update tries again.
        State.Apps[application] = new FetchedApp { Repository = repository, Revision = kept.Count == 0 ? revision : fetched?.Revision ?? 0, Hashes = hashes };
        Save();

        foreach (var path in kept)
            Console.WriteLine($"Kept `{path}`, it has local changes, use `source update {application} --force` to replace it");

        Console.WriteLine(kept.Count == 0
            ? $"{application}: revision {revision} from {repository} in {directory}"
            : $"{application}: stays at revision {fetched?.Revision ?? 0}, revision {revision} needs the kept files replaced");
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
        catch (Exception e) when (e is InvalidOperationException or HttpRequestException or IOException or System.Text.Json.JsonException)
        {
            error = e.Message;
            return null;
        }
    }

    private RepositoryIndex GetIndex(string repository)
    {
        if (indexes.TryGetValue(repository, out var cached))
            return cached;

        var content = Download(repository, RepositoryIndex.FileName)
            ?? throw new InvalidOperationException($"{repository} has no {RepositoryIndex.FileName}, is it a manifest repository?");
        var index = JsonStorage.Parse<RepositoryIndex>(content);

        if (index.Format > ToolSource.SupportedFormat)
            throw new InvalidOperationException($"{repository} has index format {index.Format}, this devtools supports up to {ToolSource.SupportedFormat}, update devtools");

        return indexes[repository] = index;
    }

    private static string Normalize(string repository)
    {
        if (Path.IsPathRooted(repository))
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(repository));

        if (!GitHubRepository().IsMatch(repository))
            throw new InvalidOperationException($"`{repository}` is neither owner/repo on GitHub nor a full directory path");

        return repository;
    }

    private static string Hash(byte[] content)
    {
        return Convert.ToHexString(SHA256.HashData(content));
    }

    private void Save()
    {
        storage.Save(FileName, State);
    }

    [GeneratedRegex(@"^[\w.-]+/[\w.-]+$")]
    private static partial Regex GitHubRepository();
}
