using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DevTools.App
{
    public class ToolDefinitionProvider : IToolDefinitionProvider
    {
        private const string ManifestFileName = ".app";

        private static readonly string FileName = PathUtils.GetRootedPath("cache.json");

        private static readonly EnumerationOptions ScanOptions = new()
        {
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = true
        };

        private readonly IStorage storage;

        private Dictionary<string, List<ToolDefinition>> tools;
        private Dictionary<string, List<ToolDefinition>> Tools => tools ??= storage.Load<Dictionary<string, List<ToolDefinition>>>(FileName);

        public ToolDefinitionProvider(IStorage storage)
        {
            this.storage = storage;
        }

        public List<string> GetNames()
        {
            return Tools.Keys.OrderBy(i => i).ToList();
        }

        public List<ToolDefinition> GetVersions(string application)
        {
            return Tools.TryGetValue(application, out var versions) 
                ? versions.OrderBy(i => Version.TryParse(i.Manifest.Version, out var v) ? v : new Version()).ToList() 
                : new List<ToolDefinition>();
        }

        public ToolDefinition GetVersion(string application, string version)
        {
            var versions = GetVersions(application);

            var exact = versions.SingleOrDefault(i => i.Manifest.Version == version);
            if (exact != null)
                return exact;

            return version switch
            {
                MetaVersion.Latest => versions.LastOrDefault(),
                MetaVersion.Oldest => versions.FirstOrDefault(),
                _ => null
            };
        }

        public void Discover(string root)
        {
            if (root == null) 
                throw new ArgumentNullException(nameof(root));

            var directory = new DirectoryInfo(root);
            if(!directory.Exists)
                throw new ArgumentException($"Directory do not exists `{root}`", nameof(root));

            var apps = Scan(directory).SelectMany(CreateDefinitions).ToList();

            ThrowOnDuplicateVersions(apps);

            tools = apps.GroupBy(i => i.Manifest.Name).ToDictionary(i => i.Key, i => i.ToList());
            storage.Save(FileName, tools);
        }

        public string FindSourceDirectory(string root, string application)
        {
            var directories = Scan(new DirectoryInfo(root))
                .Where(i => i.Manifest.Name == application && File.Exists(Path.Combine(i.Directory.FullName, ToolInstaller.SourceFileName)))
                .ToList();

            if (directories.Count == 0)
                throw new InvalidOperationException($"No `{ToolInstaller.SourceFileName}` file found for {application}");

            if (directories.Count > 1)
                throw new InvalidOperationException($"More than one `{ToolInstaller.SourceFileName}` file found for {application}: {directories.Select(i => i.Directory.FullName).Join(", ")}");

            var (directory, manifest) = directories[0];
            if (!manifest.Versioned)
                throw new InvalidOperationException($"{application} in `{directory.FullName}` needs \"versioned\": true to install versions");

            return directory.FullName;
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

            if (!manifest.Versioned)
            {
                if (string.IsNullOrWhiteSpace(manifest.Version))
                    throw new InvalidOperationException($"Manifest in `{directory.FullName}` has no version and is not versioned");

                yield return CreateDefinition(directory, manifest, manifest.Version.Replace("%DIRNAME%", directory.Name));
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
                    Paths = manifest.Paths,
                    Envs = manifest.Envs
                }
            };
        }

        private static void ThrowOnDuplicateVersions(IEnumerable<ToolDefinition> apps)
        {
            var duplicate = apps
                .GroupBy(i => (i.Manifest.Name, i.Manifest.Version))
                .FirstOrDefault(i => i.Count() > 1);

            if (duplicate != null)
                throw new InvalidOperationException(
                    $"Version {duplicate.Key.Version} of {duplicate.Key.Name} is defined more than once: {duplicate.Select(i => i.Path).Join(", ")}");
        }
    }
}