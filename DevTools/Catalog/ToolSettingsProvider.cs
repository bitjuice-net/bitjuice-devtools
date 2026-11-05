using System;
using System.Collections.Generic;
using System.Linq;
using DevTools.Infrastructure;

namespace DevTools.Catalog
{
    public class ToolSettingsProvider : IToolSettingsProvider
    {
        private static readonly string FileName = PathUtils.GetRootedPath("tool-settings.json");

        private readonly IStorage storage;
        private readonly IToolDefinitionProvider definitionProvider;

        private Dictionary<string, ToolSettings> applications;
        private Dictionary<string, ToolSettings> Applications => applications ??= storage.Load<Dictionary<string, ToolSettings>>(FileName);

        public ToolSettingsProvider(IStorage storage, IToolDefinitionProvider definitionProvider)
        {
            this.storage = storage;
            this.definitionProvider = definitionProvider;
        }

        public ToolSettings GetSettings(string application)
        {
            if (!Applications.TryGetValue(application, out var config))
            {
                config = new ToolSettings();

                if (definitionProvider.GetVersions(application).Any())
                    config.Version = MetaVersion.Latest;

                Applications.Add(application, config);
            }

            return config;
        }

        public void UpdateSettings(string application, Action<ToolSettings> action)
        {
            var settings = GetSettings(application);
            action(settings);
            storage.Save(FileName, Applications);
        }

        public List<string> GetApplications()
        {
            return Applications.Keys.ToList();
        }

        public void RemoveSettings(string application)
        {
            if (Applications.Remove(application))
                storage.Save(FileName, Applications);
        }
    }
}