using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DevTools.App
{
    public class ToolDefinitionProvider : IToolDefinitionProvider
    {
        private static readonly string FileName = PathUtils.GetRootedPath("cache.json");

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

            var apps = directory
                .EnumerateFiles(".app", SearchOption.AllDirectories)
                .Select(fileInfo => new ToolDefinition
                {
                    Path = fileInfo.DirectoryName,
                    Manifest = storage.Load<ToolManifest>(fileInfo.FullName)
                })
                .ToList();

            

            foreach (var app in apps)
            {
                var dirName = Path.GetFileName(app.Path);
                app.Manifest.Version = app.Manifest.Version.Replace("%DIRNAME%", dirName);
            }

            tools = apps.GroupBy(i => i.Manifest.Name).ToDictionary(i => i.Key, i => i.ToList());
            storage.Save(FileName, tools);
        }
    }
}