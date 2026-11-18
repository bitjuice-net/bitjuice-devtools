using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DevTools.Catalog;
using DevTools.Infrastructure;
using DevTools.Installation;
using DevTools.Sources;
using Microsoft.Extensions.Options;

namespace DevTools.Management;

public class ToolManager(
    IOptions<Settings> options,
    IToolDefinitionProvider toolDefinitionProvider,
    IToolSettingsProvider toolSettingsProvider,
    Lazy<IToolInstaller> installer,
    Lazy<ISourceManager> sources = null)
    : IToolManager
{
    private const string NotSetConst = "<not set>";

    private readonly Settings settings = options.Value;

    // Created on first use, only install, update and remove download anything.
    private IToolInstaller toolInstaller => installer.Value;

    public void GetEnv(Shell shell)
    {
        EnsureScanned();

        var pathBuilder = new PathBuilder();
        var envsBuilder = new EnvsBuilder();

        foreach (var version in GetTools())
        {
            pathBuilder.AddApplication(version);
            envsBuilder.AddApplication(version);
        }

        // devtools itself (and dt.cmd next to it) stays on PATH after the tools.
        var paths = pathBuilder.Values.Append(PathUtils.AssemblyDirectory);

        Console.WriteLine(ShellScripts.Env(shell, paths, envsBuilder.Values, Environment.GetEnvironmentVariable, JsonStorage.ReadState(Path.Combine(PathUtils.AssemblyDirectory, PathUtils.DataDirectoryName))));
    }

    public void Scan()
    {
        toolDefinitionProvider.Scan(PathUtils.GetRootedPath(settings.RootPath));
    }

    // Changes made by hand, such as a new version directory or an edited .app, are found without `sc`.
    // A failed scan must not break the console, so the previous list stays and the next command tries again.
    private void EnsureScanned()
    {
        var root = PathUtils.GetRootedPath(settings.RootPath);
        if (!Directory.Exists(root) || toolDefinitionProvider.IsUpToDate(root))
            return;

        try
        {
            toolDefinitionProvider.Scan(root);
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine($"Could not scan {root}, using the previous list of applications: {e.Message}");
        }
    }

    public void Prune()
    {
        Scan();

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

    public void Install(string application, string version, bool refresh = false)
    {
        Scan();

        var directory = FindOrFetchSourceDirectory(application);
        var target = FindRemoteVersion(directory, version, refresh);

        // A version released since the versions were cached is not in the cache yet.
        if (target == null && !refresh)
            target = FindRemoteVersion(directory, version, true);

        if (target == null)
            throw new InvalidOperationException($"Version {version} of {application} not found");

        if (toolDefinitionProvider.GetVersions(application).Any(i => VersionNumber.AreEqual(i.Manifest.Version, target.Version)))
        {
            Console.WriteLine($"{application} {target.Version} is already installed");
            return;
        }

        InstallVersion(application, directory, target);
    }

    private RemoteVersion FindRemoteVersion(string directory, string version, bool refresh)
    {
        var available = toolInstaller.GetVersionsAsync(directory, refresh).GetAwaiter().GetResult();

        return version == MetaVersion.Latest
            ? available.FirstOrDefault()
            : available.FirstOrDefault(i => VersionNumber.AreEqual(i.Version, version));
    }

    public void Update(string application, bool refresh = false)
    {
        Scan();
        UpdateOne(application, refresh);
    }

    // Every application with a .source, one that fails does not stop the others.
    public void UpdateAll(bool refresh = false)
    {
        Scan();

        var names = toolDefinitionProvider.GetNames().Where(HasSource).ToList();
        if (names.Count == 0)
        {
            Console.WriteLine($"No application has a `{ToolSource.FileName}` file, `app install` gets one from the sources");
            return;
        }

        var updated = new List<string>();
        var failed = new List<string>();

        foreach (var name in names)
        {
            try
            {
                if (UpdateOne(name, refresh))
                    updated.Add(name);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"{name}: {e.Message}");
                failed.Add(name);
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Updated {updated.Count}, up to date {names.Count - updated.Count - failed.Count}, failed {failed.Count} of {names.Count} application(s)" +
            (updated.Count > 0 ? $": {updated.Join(", ")}" : ""));

        if (failed.Count > 0)
            throw new InvalidOperationException($"Could not update {failed.Join(", ")}");
    }

    private bool HasSource(string application)
    {
        var definition = toolDefinitionProvider.GetVersions(application).FirstOrDefault();
        return definition is { Manifest.Versioned: true } && File.Exists(Path.Combine(Path.GetDirectoryName(definition.Path)!, ToolSource.FileName));
    }

    private bool UpdateOne(string application, bool refresh)
    {
        var directory = FindOrFetchSourceDirectory(application);
        var newest = toolInstaller.GetVersionsAsync(directory, refresh).GetAwaiter().GetResult().FirstOrDefault();

        if (newest == null)
            throw new InvalidOperationException($"No versions of {application} found");

        var installed = toolDefinitionProvider.GetVersion(application, MetaVersion.Latest);

        if (installed != null && VersionNumber.TryParse(installed.Manifest.Version, out var current) && VersionNumber.TryParse(newest.Version, out var available) && available <= current)
        {
            Console.WriteLine($"{application} is up to date, the newest installed version is {installed.Manifest.Version} and the newest available is {newest.Version}");
            return false;
        }

        InstallVersion(application, directory, newest);
        return true;
    }

    public void Remove(string application, string version)
    {
        Scan();

        var installed = FindVersions(application);
        var definition = installed.SingleOrDefault(i => VersionNumber.AreEqual(i.Manifest.Version, version))
            ?? throw new InvalidOperationException($"Version {version} of {application} not found, installed: {installed.Select(i => i.Manifest.Version).Join(", ")}");

        if (!definition.Manifest.Versioned)
            throw new InvalidOperationException($"{application} in `{definition.Path}` is not versioned, only versions of versioned applications can be removed");

        var selected = toolSettingsProvider.GetSettings(application).Version;

        if (VersionNumber.AreEqual(selected, version))
            throw new InvalidOperationException($"{application} {version} is selected, use `sl {application}` to select another version first");

        toolInstaller.Remove(definition.Path);
        Scan();

        Console.WriteLine($"Removed {application} {version} from {definition.Path}");
        Console.WriteLine(toolDefinitionProvider.GetVersions(application).Any()
            ? $"{application} uses {FormatVersion(application, selected)}"
            : $"{application} has no installed versions left");
    }

    // Manifests from the sources are brought to their synced revision first, an application without them gets them.
    private string FindOrFetchSourceDirectory(string application)
    {
        if (sources != null && sources.Value.Refresh(application))
            Scan();

        return toolDefinitionProvider.FindSourceDirectory(PathUtils.GetRootedPath(settings.RootPath), application);
    }

    private void InstallVersion(string application, string directory, RemoteVersion target)
    {
        var path = toolInstaller.InstallAsync(directory, target).GetAwaiter().GetResult();
        Scan();

        Console.WriteLine($"Installed {application} {target.Version} in {path}");

        var selected = toolSettingsProvider.GetSettings(application).Version;
        Console.WriteLine(toolDefinitionProvider.GetVersion(application, selected)?.Manifest.Version == target.Version
            ? $"{application} now uses {target.Version}"
            : $"{application} still uses {FormatVersion(application, selected)}, use `sl {application} {target.Version}` to switch");
    }

    public void List(bool all)
    {
        EnsureScanned();
        var table = new TextTable("NAME", "SELECTED", "INSTALLED", "STATUS");

        var names = toolDefinitionProvider.GetNames();
        if (all)
            names = names.Union(toolSettingsProvider.GetApplications()).OrderBy(i => i).ToList();

        foreach (var name in names)
        {
            var toolSettings = toolSettingsProvider.GetSettings(name);

            if (IsNotVersioned(name))
                table.AddRow(name, "-", "-", GetStatus(name, toolSettings));
            else
                table.AddRow(name, FormatVersion(name, toolSettings.Version), SummarizeVersions(name), GetStatus(name, toolSettings));
        }

        table.Write();
    }

    // Many versions would make the column too wide, `ls <application>` lists all of them.
    private string SummarizeVersions(string application)
    {
        var versions = toolDefinitionProvider.GetVersions(application).Select(i => i.Manifest.Version).ToList();
        return versions.Count <= 3 ? versions.Join(", ") : $"{versions[0]} - {versions[^1]} ({versions.Count} versions)";
    }

    public void Show(string application)
    {
        var versions = FindVersions(application);
        var toolSettings = toolSettingsProvider.GetSettings(application);
        var selected = toolSettings.Version != null ? toolDefinitionProvider.GetVersion(application, toolSettings.Version) : null;
        var versioned = !IsNotVersioned(application);
        var directory = versioned ? Path.GetDirectoryName(versions[0].Path) : versions[0].Path;

        var rows = new List<(string Name, string Value)>
        {
            ("status", GetStatus(application, toolSettings) is { Length: > 0 } status ? status : "enabled"),
            ("directory", directory)
        };

        if (versioned)
        {
            rows.Add(("selected", FormatVersion(application, toolSettings.Version)));
            rows.Add(("versions", versions.Select(i => i.Manifest.Version).Reverse().Join(", ")));
        }

        if (selected != null)
        {
            var paths = new PathBuilder();
            var envs = new EnvsBuilder();
            paths.AddApplication(selected);
            envs.AddApplication(selected);
            rows.AddRange(paths.Values.Select(i => ("path", i)));
            rows.AddRange(envs.Values.Select(i => ("env", $"{i.Key}={i.Value}")));
        }

        rows.Add(("source", DescribeSource(application, directory)));

        Console.WriteLine(application);
        foreach (var (name, value) in rows)
            Console.WriteLine($"  {name,-10} {value}");
    }

    private string DescribeSource(string application, string directory)
    {
        if (!File.Exists(Path.Combine(directory, ToolSource.FileName)))
            return "none, versions are added by hand";

        var origin = sources?.Value.GetOrigin(application);
        return origin != null ? $"{origin.Repository}, revision {origin.Revision}" : $"local {ToolSource.FileName}";
    }

    public void Select(string application, string version)
    {
        var installed = FindVersions(application);

        if (IsNotVersioned(application))
        {
            Console.WriteLine($"{application} is not versioned, there is no version to select");
            return;
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            // Newest first, so latest is next to the newest version and oldest next to the oldest one.
            var choices = installed.Select(i => i.Manifest.Version).Reverse().Prepend(MetaVersion.Latest).Append(MetaVersion.Oldest).ToList();

            for (var i = 0; i < choices.Count; i++)
                Console.WriteLine($"[{i}] {choices[i]}");
            Console.Write("Select version to use (Enter keeps the current one): ");
            var input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
            {
                Console.WriteLine($"{application} still uses {FormatVersion(application, toolSettingsProvider.GetSettings(application).Version)}");
                return;
            }

            if (!int.TryParse(input, out var selected) || selected < 0 || selected >= choices.Count)
                throw new InvalidOperationException($"`{input}` is not a number from 0 to {choices.Count - 1}");

            version = choices[selected];
        }

        var toolDefinition = toolDefinitionProvider.GetVersion(application, version)
            ?? throw new InvalidOperationException($"Version {version} of {application} not found, installed: {installed.Select(i => i.Manifest.Version).Join(", ")}");

        toolSettingsProvider.UpdateSettings(application, s =>
        {
            var target = MetaVersion.IsMeta(version) ? version : toolDefinition.Manifest.Version;
            Console.WriteLine($"Switching {application} from {FormatVersion(application, s.Version)} to {FormatVersion(application, target)}");
            s.Version = target;
        });
    }

    public void SetDisabled(string application, bool isDisabled)
    {
        FindVersions(application);
        toolSettingsProvider.UpdateSettings(application, s => s.IsDisabled = isDisabled);
        Console.WriteLine(isDisabled ? $"{application} is disabled, it is no longer on PATH" : $"{application} is enabled");
    }

    // The installed versions, an unknown application is an error instead of settings for nothing.
    private List<ToolDefinition> FindVersions(string application)
    {
        EnsureScanned();
        var versions = toolDefinitionProvider.GetVersions(application);
        if (versions.Count == 0)
            throw new InvalidOperationException($"{application} not found, `ls` lists the applications and `sc` finds new ones");

        return versions;
    }

    private string FormatVersion(string application, string version)
    {
        if (version == null)
            return NotSetConst;

        if (!MetaVersion.IsMeta(version))
            return version;

        var resolved = toolDefinitionProvider.GetVersion(application, version);

        if (resolved?.Manifest.Version == MetaVersion.Default)
            return MetaVersion.Default;

        return resolved == null || resolved.Manifest.Version == version ? version : $"{version} ({resolved.Manifest.Version})";
    }

    private bool IsNotVersioned(string application)
    {
        var versions = toolDefinitionProvider.GetVersions(application);
        return versions.Count == 1 && versions[0].Manifest.Version == MetaVersion.Default;
    }

    private string GetStatus(string application, ToolSettings toolSettings)
    {
        if (!toolDefinitionProvider.GetVersions(application).Any())
            return "orphaned";

        if (toolSettings.Version != null && toolDefinitionProvider.GetVersion(application, toolSettings.Version) == null)
            return "orphaned";

        // Enabled is the usual case, only what differs is worth a word.
        return toolSettings.IsDisabled ? "disabled" : "";
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