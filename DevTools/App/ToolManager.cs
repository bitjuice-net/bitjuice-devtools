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

        public void Prune()
        {
            Discover();

            var pruned = 0;

            foreach (var application in toolSettingsProvider.GetApplications())
            {
                var versions = toolDefinitionProvider.GetVersions(application);

                if (!versions.Any())
                {
                    toolSettingsProvider.RemoveSettings(application);
                    Console.WriteLine($"Removed settings of {application}: application not found");
                    pruned++;
                    continue;
                }

                var selected = toolSettingsProvider.GetSettings(application).Version;
                if (selected == null || versions.Any(i => i.Manifest.Version == selected))
                    continue;

                toolSettingsProvider.UpdateSettings(application, s => s.Version = null);
                Console.WriteLine($"Cleared selected version of {application}: {selected} not found");
                pruned++;
            }

            Console.WriteLine($"Pruned {pruned} stale setting(s)");
        }

        public void List(bool all)
        {
            var table = new ConsoleTable("Name", "Version", "Available", "Status");

            var names = toolDefinitionProvider.GetNames();
            if (all)
                names = names.Union(toolSettingsProvider.GetApplications()).OrderBy(i => i).ToList();

            foreach (var name in names)
            {
                var versions = toolDefinitionProvider.GetVersions(name).Select(i => i.Manifest.Version).ToList();
                var toolSettings = toolSettingsProvider.GetSettings(name);
                table.AddRow(name, toolSettings.Version ?? NotSetConst, versions.Join(", "), GetStatus(toolSettings, versions));
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

        private static string GetStatus(ToolSettings toolSettings, List<string> versions)
        {
            if (!versions.Any() || (toolSettings.Version != null && !versions.Contains(toolSettings.Version)))
                return "Orphaned";

            return toolSettings.IsDisabled ? "Disabled" : "Enabled";
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