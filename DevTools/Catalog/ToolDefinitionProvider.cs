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

        var found = Scan(directory).ToList();
        var apps = found.SelectMany(CreateDefinitions).ToList();

        ThrowOnDuplicateVersions(apps);

        // An application without installed versions is known too, with an empty list.
        var scannedTools = found.Select(i => i.Manifest.Name).Distinct().ToDictionary(i => i, _ => new List<ToolDefinition>());
        foreach (var app in apps)
            scannedTools[app.Manifest.Name].Add(app);

        // A scan that finds the same applications changes nothing, shells do not need to reload for it.
        if (!storage.Exists(FileName) || Fingerprint(Tools) != Fingerprint(scannedTools))
            storage.Save(FileName, scannedTools);

        tools = scannedTools;
    }

    private static string Fingerprint(Dictionary<string, List<ToolDefinition>> applications)
    {
        return applications.OrderBy(i => i.Key, StringComparer.Ordinal)
            .SelectMany(i => i.Value.Select(d => $"{i.Key}|{d.Path}|{d.Manifest.Version}|{d.Manifest.Flat}|{d.Manifest.Paths?.Join(";")}|{d.Manifest.Envs?.Select(e => e.Key + "=" + e.Value).Join(";")}").Prepend(i.Key))
            .Order(StringComparer.Ordinal)
            .Join("\n");
    }

    // Changes made by hand are found by `sc`, a deleted cache by the next command.
    public bool IsScanned()
    {
        return storage.Exists(FileName);
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

    private IEnumerable<(DirectoryInfo Directory, ToolManifest Manifest)> Scan(DirectoryInfo directory)
    {
        var manifestPath = Path.Combine(directory.FullName, ToolManifest.FileName);
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