using System;
using Microsoft.Extensions.Options;

namespace DevTools.New
{
    public class Manager
    {
        private readonly Settings settings;
        private readonly IToolDefinitionProvider toolDefinitionProvider;
        private readonly IToolSettingsProvider toolSettingsProvider;

        public Manager(IOptions<Settings> settings, IToolDefinitionProvider toolDefinitionProvider, IToolSettingsProvider toolSettingsProvider)
        {
            this.settings = settings.Value;
            this.toolDefinitionProvider = toolDefinitionProvider;
            this.toolSettingsProvider = toolSettingsProvider;
        }

        public void GetPath()
        {
            var builder = new PathBuilder(PathEx.GetRootedPath(settings.RootPath, Common.AssemblyDirectory));

            foreach (var name in toolDefinitionProvider.GetNames())
            {
                var toolSettings = toolSettingsProvider.GetSettings(name);

                if (toolSettings.IsDisabled)
                    continue;

                if (string.IsNullOrWhiteSpace(toolSettings.Version))
                    continue;

                var version = toolDefinitionProvider.GetVersion(name, toolSettings.Version);
                if(version == null)
                    continue;

                builder.AddApplication(version);
            }

            Console.WriteLine(builder.Build());
        }

        public void Discover()
        {
            toolDefinitionProvider.Discover(settings.RootPath);
        }
    }
}
