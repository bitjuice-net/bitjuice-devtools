using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DevTools.Catalog.Models;
using DevTools.Infrastructure;

namespace DevTools.Catalog;

public class ToolDefinitionProvider(IStorage storage) : IToolDefinitionProvider
{
    private static readonly string FileName = PathUtils.GetCachePath("apps.json");

    private static readonly EnumerationOptions ScanOptions = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = true
    };

    private ScanResult scanned;
    private ScanResult Scanned => scanned ??= storage.Load<ScanResult>(FileName);
    private SortedDictionary<string, ScannedApp> Tools => Scanned.Apps ?? new SortedDictionary<string, ScannedApp>();

    public List<string> GetNames()
    {
        return Tools.Keys.OrderBy(i => i).ToList();
    }

    public List<ToolDefinition> GetVersions(string application)
    {
        return Tools.TryGetValue(application, out var app)
            ? app.Versions.OrderBy(i => VersionNumber.TryParse(i.Manifest.Version, out var v) ? v : new Version()).ToList()
            : new List<ToolDefinition>();
    }

    public string GetSource(string application)
    {
        return Tools.GetValueOrDefault(application)?.Source;
    }

    public List<string> GetUnmanaged()
    {
        return Scanned.Unmanaged ?? new List<string>();
    }

    public ToolDefinition GetVersion(string application, string version)
    {
        var versions = GetVersions(application);

        var exact = versions.SingleOrDefault(i => VersionNumber.AreEqual(i.Manifest.Version, version));
        if (exact != null)
            return exact;

        return version switch
        {
            MetaVersion.Latest => versions.LastOrDefault(),
            MetaVersion.Oldest => versions.FirstOrDefault(),
            _ => null
        };
    }

    public void Scan(string root)
    {
        if (root == null) 
            throw new ArgumentNullException(nameof(root));

        var directory = new DirectoryInfo(root);
        if(!directory.Exists)
            throw new ArgumentException($"Directory do not exists `{root}`", nameof(root));

        var found = Scan(directory).ToList();
        var apps = found.SelectMany(CreateDefinitions).ToList();

        ThrowOnDuplicateVersions(apps);

        // An application without installed versions is known too, with an empty list.
        var result = new ScanResult { Apps = new SortedDictionary<string, ScannedApp>(StringComparer.Ordinal) };
        foreach (var (_, manifest) in found)
        {
            var app = result.Apps.TryGetValue(manifest.Name, out var known) ? known : result.Apps[manifest.Name] = new ScannedApp();
            app.Source ??= manifest.Origin?.Repository;
        }

        foreach (var app in apps)
            result.Apps[app.Manifest.Name].Versions.Add(app);

        // A directory named like an application, such as one left by hand next to it, is not listed twice.
        result.Unmanaged = ListUnmanaged(directory).Where(i => !result.Apps.ContainsKey(i)).ToList();

        // Only what shells load changes the state, so a scan that finds nothing new does not make them reload.
        var changesState = !IsScanned() || Fingerprint(Tools) != Fingerprint(result.Apps);
        storage.Save(FileName, result, changesState);
        scanned = result;
    }

    private static string Fingerprint(IDictionary<string, ScannedApp> applications)
    {
        return applications.OrderBy(i => i.Key, StringComparer.Ordinal)
            .SelectMany(i => i.Value.Versions.Select(d => $"{i.Key}|{d.Path}|{d.Manifest.Version}|{d.Manifest.Flat}|{d.Manifest.Paths?.Join(";")}|{d.Manifest.Envs?.Select(e => e.Key + "=" + e.Value).Join(";")}").Prepend(i.Key))
            .Order(StringComparer.Ordinal)
            .Join("\n");
    }

    // Changes made by hand are found by `sc`, a deleted cache or one of an earlier version by the next command.
    public bool IsScanned()
    {
        return storage.Exists(FileName) && Scanned.Apps != null;
    }

    public string FindSourceDirectory(string root, string application)
    {
        var directories = Scan(new DirectoryInfo(root))
            .Where(i => i.Manifest.Name == application && i.Manifest.Install != null)
            .ToList();

        if (directories.Count == 0)
            throw new InvalidOperationException($"{application} has no `{ToolManifest.FileName}` with an install section");

        if (directories.Count > 1)
            throw new InvalidOperationException($"More than one `{ToolManifest.FileName}` with an install section found for {application}: {directories.Select(i => i.Directory.FullName).Join(", ")}");

        var (directory, manifest) = directories[0];
        if (manifest.Flat)
            throw new InvalidOperationException($"{application} in `{directory.FullName}` is flat, only applications with versions can be installed");

        return directory.FullName;
    }

    // The whole .app of an application, with its install section and origin, null when there is none.
    public ToolManifest GetManifest(string root, string application)
    {
        var directory = FindDirectory(root, application);
        return directory != null ? storage.Load<ToolManifest>(Path.Combine(directory, ToolManifest.FileName)) : null;
    }

    // The directory with the .app of the application, null when there is none.
    public string FindDirectory(string root, string application)
    {
        if (!Directory.Exists(root))
            return null;

        var directories = Scan(new DirectoryInfo(root)).Where(i => i.Manifest.Name == application).ToList();

        if (directories.Count > 1)
            throw new InvalidOperationException($"More than one `{ToolManifest.FileName}` file found for {application}: {directories.Select(i => i.Directory.FullName).Join(", ")}");

        return directories.SingleOrDefault().Directory?.FullName;
    }

    // Every directory of the tools directory is an application, the ones with .app are managed.
    private IEnumerable<(DirectoryInfo Directory, ToolManifest Manifest)> Scan(DirectoryInfo root)
    {
        foreach (var child in Applications(root))
        {
            var manifestPath = Path.Combine(child.FullName, ToolManifest.FileName);
            if (File.Exists(manifestPath))
                yield return (child, storage.Load<ToolManifest>(manifestPath));
        }
    }

    private static IEnumerable<string> ListUnmanaged(DirectoryInfo root)
    {
        return Applications(root)
            .Where(i => !File.Exists(Path.Combine(i.FullName, ToolManifest.FileName)))
            .Select(i => i.Name);
    }

    // devtools itself and hidden directories, such as .git, are not applications.
    private static IEnumerable<DirectoryInfo> Applications(DirectoryInfo root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var devtools = Path.TrimEndingDirectorySeparator(PathUtils.AssemblyDirectory);

        return root.EnumerateDirectories("*", ScanOptions)
            .Where(i => !i.Name.StartsWith('.') && !string.Equals(Path.TrimEndingDirectorySeparator(i.FullName), devtools, comparison))
            .OrderBy(i => i.Name, StringComparer.Ordinal);
    }

    private static IEnumerable<ToolDefinition> CreateDefinitions((DirectoryInfo Directory, ToolManifest Manifest) app)
    {
        var (directory, manifest) = app;

        if (string.IsNullOrWhiteSpace(manifest.Name))
            throw new InvalidOperationException($"Manifest in `{directory.FullName}` has no name");

        if (manifest.Version != null)
            throw new InvalidOperationException($"Manifest in `{directory.FullName}` has a version, remove it. Versions come from its subdirectories");

        if (manifest.Flat)
        {
            yield return CreateDefinition(directory, manifest, MetaVersion.Default);
            yield break;
        }

        foreach (var child in directory.EnumerateDirectories("*", ScanOptions))
        {
            if (Version.TryParse(child.Name, out _))
                yield return CreateDefinition(child, manifest, child.Name);
        }
    }

    private static ToolDefinition CreateDefinition(DirectoryInfo directory, ToolManifest manifest, string version)
    {
        return new ToolDefinition
        {
            Path = directory.FullName,
            Manifest = new ToolManifest
            {
                Name = manifest.Name,
                Version = version,
                Flat = manifest.Flat,
                Paths = manifest.Paths,
                Envs = manifest.Envs
            }
        };
    }

    private static void ThrowOnDuplicateVersions(IEnumerable<ToolDefinition> apps)
    {
        var duplicate = apps
            .GroupBy(i => (i.Manifest.Name, Version: VersionNumber.Normalize(i.Manifest.Version)))
            .FirstOrDefault(i => i.Count() > 1);

        if (duplicate != null)
            throw new InvalidOperationException(
                $"Version {duplicate.Key.Version} of {duplicate.Key.Name} is defined more than once: {duplicate.Select(i => i.Path).Join(", ")}");
    }
}