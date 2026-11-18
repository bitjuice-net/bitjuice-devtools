using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DevTools.Catalog.Models;
using DevTools.Infrastructure;
using DevTools.Installation.Models;

namespace DevTools.Catalog;

public class ToolDefinitionProvider(IStorage storage) : IToolDefinitionProvider
{
    private const string ManifestFileName = ".app";

    private static readonly string FileName = PathUtils.GetDataPath("cache.json");
    private static readonly string SnapshotFileName = PathUtils.GetDataPath("scan.json");

    private static readonly EnumerationOptions ScanOptions = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = true
    };

    private Dictionary<string, List<ToolDefinition>> tools;
    private Dictionary<string, List<ToolDefinition>> Tools => tools ??= storage.Load<Dictionary<string, List<ToolDefinition>>>(FileName);

    public List<string> GetNames()
    {
        return Tools.Keys.OrderBy(i => i).ToList();
    }

    public List<ToolDefinition> GetVersions(string application)
    {
        return Tools.TryGetValue(application, out var versions) 
            ? versions.OrderBy(i => VersionNumber.TryParse(i.Manifest.Version, out var v) ? v : new Version()).ToList() 
            : new List<ToolDefinition>();
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

        var scanned = DateTime.UtcNow;
        var found = Scan(directory).ToList();
        var apps = found.SelectMany(CreateDefinitions).ToList();

        ThrowOnDuplicateVersions(apps);

        var scannedTools = apps.GroupBy(i => i.Manifest.Name).ToDictionary(i => i.Key, i => i.ToList());

        // A scan that finds the same applications changes nothing, shells do not need to reload for it.
        if (!storage.Exists(FileName) || Fingerprint(Tools) != Fingerprint(scannedTools))
            storage.Save(FileName, scannedTools);

        tools = scannedTools;
        storage.Save(SnapshotFileName, CreateSnapshot(directory, found.Select(i => i.Directory), scanned), changesState: false);
    }

    private static string Fingerprint(Dictionary<string, List<ToolDefinition>> applications)
    {
        return applications.OrderBy(i => i.Key, StringComparer.Ordinal)
            .SelectMany(i => i.Value.Select(d => $"{i.Key}|{d.Path}|{d.Manifest.Version}|{d.Manifest.Versioned}|{d.Manifest.Paths?.Join(";")}|{d.Manifest.Envs?.Select(e => e.Key + "=" + e.Value).Join(";")}"))
            .Order(StringComparer.Ordinal)
            .Join("\n");
    }

    // True when a scan would find the same, so changes made by hand are found without `sc`.
    public bool IsUpToDate(string root)
    {
        if (!storage.Exists(FileName))
            return false;

        // A cache written before snapshots existed stays until the next scan writes one.
        var snapshot = storage.Load<ScanSnapshot>(SnapshotFileName);
        if (snapshot.Root == null)
            return true;

        // File systems keep times in steps of up to 2 seconds, so a change made right around a scan may keep the time
        // the scan saw. Such entries cannot be trusted and get one more scan, like git does with racily clean files.
        var racy = snapshot.Scanned - TimeSpan.FromSeconds(2).Ticks;

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(snapshot.Root, Path.GetFullPath(root), comparison)
            && snapshot.Times.All(i => i.Value < racy && LastWrite(i.Key) == i.Value);
    }

    // A directory changes its time when an entry is added or removed: the tools directory and the ones on the way
    // to an application for new applications, an application directory for new versions, and .app when it is edited.
    // Other directories, such as data of devtools, change all the time and do not matter.
    private static ScanSnapshot CreateSnapshot(DirectoryInfo root, IEnumerable<DirectoryInfo> applications, DateTime scanned)
    {
        var paths = new HashSet<string> { root.FullName };

        foreach (var application in applications)
        {
            paths.Add(Path.Combine(application.FullName, ManifestFileName));
            for (var directory = application; directory != null && directory.FullName.Length > root.FullName.Length; directory = directory.Parent)
                paths.Add(directory.FullName);
        }

        return new ScanSnapshot { Root = root.FullName, Scanned = scanned.Ticks, Times = paths.ToDictionary(i => i, LastWrite) };
    }

    private static long LastWrite(string path)
    {
        // One call for files and directories alike, a missing path gives a fixed time that never matches.
        return File.GetLastWriteTimeUtc(path).Ticks;
    }

    public string FindSourceDirectory(string root, string application)
    {
        var directories = Scan(new DirectoryInfo(root))
            .Where(i => i.Manifest.Name == application && File.Exists(Path.Combine(i.Directory.FullName, ToolSource.FileName)))
            .ToList();

        if (directories.Count == 0)
            throw new InvalidOperationException($"No `{ToolSource.FileName}` file found for {application}");

        if (directories.Count > 1)
            throw new InvalidOperationException($"More than one `{ToolSource.FileName}` file found for {application}: {directories.Select(i => i.Directory.FullName).Join(", ")}");

        var (directory, manifest) = directories[0];
        if (!manifest.Versioned)
            throw new InvalidOperationException($"{application} in `{directory.FullName}` needs \"versioned\": true to install versions");

        return directory.FullName;
    }

    // The directory with the .app of the application, null when there is none.
    public string FindDirectory(string root, string application)
    {
        var directories = Scan(new DirectoryInfo(root)).Where(i => i.Manifest.Name == application).ToList();

        if (directories.Count > 1)
            throw new InvalidOperationException($"More than one `{ManifestFileName}` file found for {application}: {directories.Select(i => i.Directory.FullName).Join(", ")}");

        return directories.SingleOrDefault().Directory?.FullName;
    }

    private IEnumerable<(DirectoryInfo Directory, ToolManifest Manifest)> Scan(DirectoryInfo directory)
    {
        var manifestPath = Path.Combine(directory.FullName, ManifestFileName);
        if (File.Exists(manifestPath))
        {
            yield return (directory, storage.Load<ToolManifest>(manifestPath));
            yield break;
        }

        foreach (var child in directory.EnumerateDirectories("*", ScanOptions))
        foreach (var app in Scan(child))
            yield return app;
    }

    private static IEnumerable<ToolDefinition> CreateDefinitions((DirectoryInfo Directory, ToolManifest Manifest) app)
    {
        var (directory, manifest) = app;

        if (string.IsNullOrWhiteSpace(manifest.Name))
            throw new InvalidOperationException($"Manifest in `{directory.FullName}` has no name");

        if (manifest.Version != null)
            throw new InvalidOperationException($"Manifest in `{directory.FullName}` has a version, remove it. Versions come from subdirectories of a manifest with \"versioned\": true");

        if (!manifest.Versioned)
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
                Versioned = manifest.Versioned,
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