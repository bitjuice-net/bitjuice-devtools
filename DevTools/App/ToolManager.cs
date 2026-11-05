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
        private readonly IToolInstaller toolInstaller;

        public ToolManager(IOptions<Settings> settings, IToolDefinitionProvider toolDefinitionProvider, IToolSettingsProvider toolSettingsProvider, IToolInstaller toolInstaller)
        {
            this.toolInstaller = toolInstaller;
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
                if (selected == null || toolDefinitionProvider.GetVersion(application, selected) != null)
                    continue;

                toolSettingsProvider.UpdateSettings(application, s => s.Version = null);
                Console.WriteLine($"Cleared selected version of {application}: {selected} not found");
                pruned++;
            }

            Console.WriteLine($"Pruned {pruned} stale setting(s)");
        }

        public void Install(string application, string version)
        {
            Discover();

            var directory = toolDefinitionProvider.FindSourceDirectory(PathUtils.GetRootedPath(settings.RootPath), application);
            var available = toolInstaller.GetVersionsAsync(directory).GetAwaiter().GetResult();

            var target = version == MetaVersion.Latest
                ? available.FirstOrDefault()
                : available.FirstOrDefault(i => i.Version == version);

            if (target == null)
                throw new InvalidOperationException($"Version {version} of {application} not found");

            if (toolDefinitionProvider.GetVersions(application).Any(i => i.Manifest.Version == target.Version))
            {
                Console.WriteLine($"{application} {target.Version} is already installed");
                return;
            }

            InstallVersion(application, directory, target);
        }

        public void Update(string application)
        {
            Discover();

            var directory = toolDefinitionProvider.FindSourceDirectory(PathUtils.GetRootedPath(settings.RootPath), application);
            var newest = toolInstaller.GetVersionsAsync(directory).GetAwaiter().GetResult().FirstOrDefault();

            if (newest == null)
                throw new InvalidOperationException($"No versions of {application} found");

            var installed = toolDefinitionProvider.GetVersion(application, MetaVersion.Latest);

            if (installed != null && Version.TryParse(installed.Manifest.Version, out var current) && Version.Parse(newest.Version) <= current)
            {
                Console.WriteLine($"{application} is up to date, the newest installed version is {installed.Manifest.Version} and the newest available is {newest.Version}");
                return;
            }

            InstallVersion(application, directory, newest);
        }

        private void InstallVersion(string application, string directory, RemoteVersion target)
        {
            var path = toolInstaller.InstallAsync(directory, target).GetAwaiter().GetResult();
            Discover();

            Console.WriteLine($"Installed {application} {target.Version} in {path}");

            var selected = toolSettingsProvider.GetSettings(application).Version;
            Console.WriteLine(toolDefinitionProvider.GetVersion(application, selected)?.Manifest.Version == target.Version
                ? $"{application} now uses {target.Version}"
                : $"{application} still uses {FormatVersion(application, selected)}, use `select {application} {target.Version}` to switch");
        }

        public void List(bool all)
        {
            var table = new ConsoleTable("Name", "Version", "Installed", "Status");

            var names = toolDefinitionProvider.GetNames();
            if (all)
                names = names.Union(toolSettingsProvider.GetApplications()).OrderBy(i => i).ToList();

            foreach (var name in names)
            {
                var versions = toolDefinitionProvider.GetVersions(name).Select(i => i.Manifest.Version).ToList();
                var toolSettings = toolSettingsProvider.GetSettings(name);
                table.AddRow(name, FormatVersion(name, toolSettings.Version), versions.Join(", "), GetStatus(name, toolSettings));
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

                var choices = versions.Select(i => i.Manifest.Version).Concat(new[] { MetaVersion.Latest, MetaVersion.Oldest }).ToList();

                for (var i = 0; i < choices.Count; i++)
                    Console.WriteLine($"[{i}] {choices[i]}");
                Console.Write("Select version to use: ");
                var input = Console.ReadLine();

                if (!int.TryParse(input, out var selected))
                    throw new Exception($"'{input}' is not a valid index");

                if (selected < 0 || selected >= choices.Count)
                    throw new Exception("Selected index is out of range");

                version = choices[selected];
            }

            var toolDefinition = toolDefinitionProvider.GetVersion(application, version);
            if (toolDefinition == null)
                return;

            toolSettingsProvider.UpdateSettings(application, s =>
            {
                var target = MetaVersion.IsMeta(version) ? version : toolDefinition.Manifest.Version;
                Console.WriteLine($"Switching {application} from {FormatVersion(application, s.Version)} to {FormatVersion(application, target)}");
                s.Version = target;
            });
        }

        public void SetDisabled(string application, bool isDisabled)
        {
            toolSettingsProvider.UpdateSettings(application, s => s.IsDisabled = isDisabled);
        }

        private string FormatVersion(string application, string version)
        {
            if (version == null)
                return NotSetConst;

            if (!MetaVersion.IsMeta(version))
                return version;

            var resolved = toolDefinitionProvider.GetVersion(application, version);
            return resolved == null || resolved.Manifest.Version == version ? version : $"{version} ({resolved.Manifest.Version})";
        }

        private string GetStatus(string application, ToolSettings toolSettings)
        {
            if (!toolDefinitionProvider.GetVersions(application).Any())
                return "Orphaned";

            if (toolSettings.Version != null && toolDefinitionProvider.GetVersion(application, toolSettings.Version) == null)
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