﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DevTools.App
{
    public class ToolDefinitionProvider : IToolDefinitionProvider
    {
        private readonly IToolDefinitionCache cache;
        private Dictionary<string, List<ToolDefinition>> tools;
        private Dictionary<string, List<ToolDefinition>> Tools
        {
            get => tools ??= cache.Load();
            set { tools = value; cache.Save(tools); }
        }

        public ToolDefinitionProvider(IToolDefinitionCache cache)
        {
            this.cache = cache;
        }

        public List<string> GetNames()
        {
            return Tools.Keys.ToList();
        }

        public List<ToolDefinition> GetVersions(string name)
        {
            return Tools.TryGetValue(name, out var versions) ? versions : new List<ToolDefinition>();
        }

        public ToolDefinition GetVersion(string name, string version)
        {
            return GetVersions(name).SingleOrDefault(i => i.Manifest.Version == version);
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
                    Manifest = JsonEx.DeserializeFromFile<ToolManifest>(fileInfo.FullName)
                })
                .ToList();

            Tools = apps.GroupBy(i => i.Manifest.Name).ToDictionary(i => i.Key, i => i.ToList());
        }
    }
}