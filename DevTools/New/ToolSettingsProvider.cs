using System.Collections.Generic;
using System.Linq;

namespace DevTools.New
{
    public class ToolSettingsProvider : IToolSettingsProvider
    {
        private readonly IToolDefinitionProvider definitionProvider;
        public Dictionary<string, ToolSettings> Applications { get; set; }

        public ToolSettingsProvider(IToolDefinitionProvider definitionProvider)
        {
            this.definitionProvider = definitionProvider;
        }

        public ToolSettings GetSettings(string name)
        {
            if (!Applications.TryGetValue(name, out var config))
            {
                var versions = definitionProvider.GetVersions(name);
                if (!versions.Any())
                    return null;

                config = new ToolSettings
                {
                    Version = versions.First().Manifest.Version
                };

                Applications.Add(name, config);
            }

            return config;
        }
    }
}