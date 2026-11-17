using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DevTools.Catalog;
using DevTools.Infrastructure;
using DevTools.Installation;
using Microsoft.Extensions.Options;

namespace DevTools.Sources;

public interface ISourceManager
{
    void Add(string repository);
    void Remove(string repository);
    void List(string repository);
    void Sync();

    // Where the manifests of an application came from, null when they were not fetched from a source.
    FetchedApp GetOrigin(string application);

    // Brings .app and .source of an application to the revision in the synced index, before it is installed or updated.
    // Returns true when a file changed.
    bool Refresh(string application);
}

// Manifest repositories: a directory per application with .app and .source, and index.json with their revisions.
// The indexes are kept in data/indexes.json, `source sync` gets them again.
public partial class SourceManager(IOptions<Settings> options, IStorage storage, IToolDefinitionProvider definitions, IRepositoryClient client)
    : ISourceManager
{
    private const string ManifestFileName = ".app";
    private static readonly string[] ManifestFiles = { ManifestFileName, ToolSource.FileName };
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;
    private static readonly string FileName = PathUtils.GetDataPath("sources.json");
    private static readonly string IndexesFileName = PathUtils.GetDataPath("indexes.json");

    private readonly Settings settings = options.Value;

    private SourcesState state;
    private SourcesState State => state ??= storage.Load<SourcesState>(FileName);

    private Dictionary<string, RepositoryIndex> indexes;
    private Dictionary<string, RepositoryIndex> Indexes => indexes ??= new Dictionary<string, RepositoryIndex>(storage.Load<Dictionary<string, RepositoryIndex>>(IndexesFileName), Comparer);

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
        Indexes[repository] = index;
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

        var table = new TextTable("APPLICATION", "REVISION", "FETCHED");

        foreach (var (name, entry) in GetIndex(source).Apps)
        {
            var fetched = State.Apps.GetValueOrDefault(name);
            table.AddRow(name, entry.Revision.ToString(), fetched != null && Comparer.Equals(fetched.Repository, source) ? fetched.Revision.ToString() : "");
        }

        table.Write();
    }

    public FetchedApp GetOrigin(string application)
    {
        return State.Apps.GetValueOrDefault(application);
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
                var index = Indexes[repository] = DownloadIndex(repository);
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
        var hasSource = directory != null && File.Exists(Path.Combine(directory, ToolSource.FileName));
        var fetched = State.Apps.GetValueOrDefault(application);

        var located = Locate(application);
        if (located == null)
        {
            if (hasSource)
                return false;

            throw new InvalidOperationException($"No `{ToolSource.FileName}` file found for {application}, locally or in sources: {State.Repositories.Join(", ")}. " +
                "`source sync` gets their newest lists");
        }

        var (repository, entry) = located.Value;

        // Manifests not fetched from a source are at their own revision, 1 when they have none,
        // and a newer revision in a source replaces them. From then on the source manages the application.
        if (hasSource && fetched == null)
        {
            var local = Math.Max(1, storage.Load<ToolSource>(Path.Combine(directory, ToolSource.FileName)).Revision);
            return entry.Revision > local && Write(application, repository, entry.Revision, directory, replaceLocal: true);
        }

        if (hasSource && Comparer.Equals(fetched.Repository, repository) && entry.Revision <= fetched.Revision)
            return false;

        return Write(application, repository, entry.Revision, directory ?? Path.Combine(root, application));
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

    // The repository the application was fetched from keeps it. Otherwise the source with the newest revision wins,
    // the first one of them when they are equal.
    private (string Repository, IndexEntry Entry)? Locate(string application)
    {
        var fetched = State.Apps.GetValueOrDefault(application)?.Repository;
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

    // Replaces a local file only when it is still the one fetched before, so changes made by hand stay,
    // unless the local files were not fetched from a source at all.
    private bool Write(string application, string repository, int revision, string directory, bool replaceLocal = false)
    {
        var files = ManifestFiles.ToDictionary(i => i, i => Download(repository, $"{application}/{i}")
            ?? throw new InvalidOperationException($"{repository} lists {application} but has no `{application}/{i}`, `source sync` gets its newest list"));

        Validate(JsonStorage.Parse<ToolManifest>(files[ManifestFileName]), JsonStorage.Parse<ToolSource>(files[ToolSource.FileName]), application, $"{repository}/{application}");
        Directory.CreateDirectory(directory);

        var fetched = State.Apps.GetValueOrDefault(application);
        var hashes = new Dictionary<string, string>();
        var kept = new List<string>();
        var changed = false;

        foreach (var (file, content) in files)
        {
            var path = Path.Combine(directory, file);
            var hash = Hash(content);
            var local = File.Exists(path) ? Hash(File.ReadAllBytes(path)) : null;

            if (!replaceLocal && local != null && local != hash && local != fetched?.Hashes.GetValueOrDefault(file))
            {
                kept.Add(path);
                if (fetched?.Hashes.GetValueOrDefault(file) is { } previous)
                    hashes[file] = previous;
                continue;
            }

            if (local != hash)
            {
                File.WriteAllBytes(path, content);
                changed = true;
            }
            hashes[file] = hash;
        }

        // With a file kept the application is not at the new revision, so the next install or update tries again.
        State.Apps[application] = new FetchedApp { Repository = repository, Revision = kept.Count == 0 ? revision : fetched?.Revision ?? 0, Hashes = hashes };
        Save();

        foreach (var path in kept)
            Console.WriteLine($"Kept `{path}`, it has local changes, delete it to get revision {revision} from {repository}");

        if (kept.Count == 0)
            Console.WriteLine($"{application}: manifests at revision {revision} from {repository}");

        return changed;
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

    // The synced index, a repository that was never synced is synced on first use.
    private RepositoryIndex GetIndex(string repository)
    {
        if (Indexes.TryGetValue(repository, out var cached))
            return cached;

        var index = Indexes[repository] = DownloadIndex(repository);
        SaveIndexes();
        return index;
    }

    private RepositoryIndex DownloadIndex(string repository)
    {
        var content = Download(repository, RepositoryIndex.FileName)
            ?? throw new InvalidOperationException($"{repository} has no {RepositoryIndex.FileName}, is it a manifest repository?");
        var index = JsonStorage.Parse<RepositoryIndex>(content);

        if (index.Format > ToolSource.SupportedFormat)
            throw new InvalidOperationException($"{repository} has index format {index.Format}, this devtools supports up to {ToolSource.SupportedFormat}, update devtools");

        return index;
    }

    private static bool IsUnavailable(Exception e)
    {
        return e is InvalidOperationException or HttpRequestException or IOException or System.Text.Json.JsonException;
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

    private void SaveIndexes()
    {
        storage.Save(IndexesFileName, Indexes);
    }

    [GeneratedRegex(@"^[\w.-]+/[\w.-]+$")]
    private static partial Regex GitHubRepository();
}
