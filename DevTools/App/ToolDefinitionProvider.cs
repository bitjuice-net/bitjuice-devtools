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
            return Tools.TryGetValue(application, out var versions) ? versions : new List<ToolDefinition>();
        }

        public ToolDefinition GetVersion(string application, string version)
        {
            return GetVersions(application).SingleOrDefault(i => i.Manifest.Version == version);
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

            tools = apps.GroupBy(i => i.Manifest.Name).ToDictionary(i => i.Key, i => i.ToList());
            storage.Save(FileName, tools);
        }
    }
}