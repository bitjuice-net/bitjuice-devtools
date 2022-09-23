using System;
using System.Collections.Generic;
using System.Linq;
using ConsoleTables;
using Microsoft.Extensions.Options;

namespace DevTools.App
{
    public class ToolManager : IToolManager
    {
        private const string NotSetConst = "<not set>";

        private readonly Settings settings;
        private readonly IToolDefinitionProvider toolDefinitionProvider;
        private readonly IToolSettingsProvider toolSettingsProvider;

        public ToolManager(IOptions<Settings> settings, IToolDefinitionProvider toolDefinitionProvider, IToolSettingsProvider toolSettingsProvider)
        {
            this.settings = settings.Value;
            this.toolDefinitionProvider = toolDefinitionProvider;
            this.toolSettingsProvider = toolSettingsProvider;
        }

        public void GetSetup()
        {
            var pathBuilder = new PathBuilder();
            var envsBuilder = new EnvsBuilder();

            foreach (var version in GetTools())
            {
                pathBuilder.AddApplication(version);
                envsBuilder.AddApplication(version);
            }

            Console.WriteLine("DT_PATH=" + pathBuilder.Build());
            Console.WriteLine(envsBuilder.Build());
        }

        public void GetPath()
        {
            var builder = new PathBuilder();

            foreach (var version in GetTools())
                builder.AddApplication(version);

            Console.WriteLine(builder.Build());
        }

        public void GetEnvs()
        {
            var builder = new EnvsBuilder();

            foreach (var version in GetTools())
                builder.AddApplication(version);

            Console.WriteLine(builder.Build());
        }

        public void Discover()
        {
            toolDefinitionProvider.Discover(PathUtils.GetRootedPath(settings.RootPath));
        }

        public void List()
        {
            var table = new ConsoleTable("Name", "Version", "Available", "Disabled");

            foreach (var name in toolDefinitionProvider.GetNames())
            {
                var versions = toolDefinitionProvider.GetVersions(name).Select(i => i.Manifest.Version).Join(", ");
                var toolSettings = toolSettingsProvider.GetSettings(name);
                table.AddRow(name, toolSettings.Version ?? NotSetConst, versions, toolSettings.IsDisabled);
            }

            Console.WriteLine("List of applications:");
            Console.WriteLine();

            table.Write(Format.MarkDown);
        }

        public void Select(string application, string version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                var versions = toolDefinitionProvider.GetVersions(application).ToList();
                if (!versions.Any())
                    return;

                for (var i = 0; i < versions.Count; i++)
                    Console.WriteLine($"[{i}] {versions[i].Manifest.Version}");
                Console.Write("Select version to use: ");
                var input = Console.ReadLine();

                if (!int.TryParse(input, out var selected))
                    throw new Exception($"'{input}' is not a valid index");

                if (selected < 0 || selected >= versions.Count)
                    throw new Exception("Selected index is out of range");

                version = versions[selected].Manifest.Version;
            }

            var toolDefinition = toolDefinitionProvider.GetVersion(application, version);
            if (toolDefinition == null)
                return;

            toolSettingsProvider.UpdateSettings(application, s =>
            {
                Console.WriteLine($"Switching {application} from {s.Version ?? NotSetConst} to {toolDefinition.Manifest.Version}");
                s.Version = toolDefinition.Manifest.Version;
            });
        }

        public void SetDisabled(string application, bool isDisabled)
        {
            toolSettingsProvider.UpdateSettings(application, s => s.IsDisabled = isDisabled);
        }

        private IEnumerable<ToolDefinition> GetTools()
        {
            foreach (var name in toolDefinitionProvider.GetNames())
            {
                var toolSettings = toolSettingsProvider.GetSettings(name);

                if (toolSettings.IsDisabled)
                    continue;

                if (string.IsNullOrWhiteSpace(toolSettings.Version))
                    continue;

                var version = toolDefinitionProvider.GetVersion(name, toolSettings.Version);
                if (version == null)
                    continue;

                yield return version;
            }
        }
    }
}