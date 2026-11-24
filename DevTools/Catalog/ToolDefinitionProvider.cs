using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DevTools.Catalog.Models;
using DevTools.Infrastructure;

namespace DevTools.Catalog;

// What is in the tools directory, found by a scan in every command. Every directory is an application, one with .app is
// managed by devtools, one without it is only listed.
public class ToolDefinitionProvider(IStorage storage) : IToolDefinitionProvider
{
    private static readonly EnumerationOptions ScanOptions = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = true
    };

    private ScanResult scanned = new();
    private SortedDictionary<string, ScannedApp> Tools => scanned.Apps;

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
        return scanned.Unmanaged;
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

    // A broken .app is skipped with a warning, so one application does not stop devtools for the others.
    public void Scan(string root)
    {
        if (root == null)
            throw new ArgumentNullException(nameof(root));

        var directory = new DirectoryInfo(root);
        var result = new ScanResult { Apps = new SortedDictionary<string, ScannedApp>(StringComparer.Ordinal) };

        if (!directory.Exists)
        {
            scanned = result;
            return;
        }

        var found = new List<(DirectoryInfo Directory, ToolManifest Manifest, List<ToolDefinition> Versions)>();
        foreach (var child in Applications(directory).Where(i => File.Exists(Path.Combine(i.FullName, ToolManifest.FileName))))
        {
            try
            {
                var manifest = storage.Load<ToolManifest>(Path.Combine(child.FullName, ToolManifest.FileName));
                found.Add((child, manifest, CreateDefinitions((child, manifest)).ToList()));
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                Console.Error.WriteLine($"Skipped `{child.FullName}`: {e.Message}");
            }
        }

        // Two directories of one application with the same version cannot both be used, so the application is skipped.
        foreach (var application in found.GroupBy(i => i.Manifest.Name))
        {
            var duplicate = application.SelectMany(i => i.Versions)
                .GroupBy(i => VersionNumber.Normalize(i.Manifest.Version))
                .FirstOrDefault(i => i.Count() > 1);

            if (duplicate != null)
            {
                Console.Error.WriteLine($"Skipped {application.Key}: version {duplicate.Key} is defined more than once: {duplicate.Select(i => i.Path).Join(", ")}");
                continue;
            }

            // An application without installed versions is known too, with an empty list.
            result.Apps[application.Key] = new ScannedApp
            {
                Source = application.Select(i => i.Manifest.Origin?.Repository).FirstOrDefault(i => i != null),
                Versions = application.SelectMany(i => i.Versions).ToList()
            };
        }

        // A directory named like an application, such as one left by hand next to it, is not listed twice.
        result.Unmanaged = ListUnmanaged(directory).Where(i => !result.Apps.ContainsKey(i)).ToList();
        scanned = result;
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
}
