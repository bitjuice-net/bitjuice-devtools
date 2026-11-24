using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text;
using DevTools.Catalog;
using DevTools.Catalog.Models;
using DevTools.Infrastructure;
using DevTools.Installation;
using DevTools.Installation.Models;
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

    // Writes the script of the shell when it changed and prints it. init.cmd and init.sh run `shell env` when the script
    // is missing or older than the state.
    public void GetEnv(Shell shell)
    {
        Console.Write(WriteEnvScript(shell));
    }

    // After every command. What shells load can also change without devtools, such as a version copied in by hand, so the
    // script is built every time and written, with a new state that makes shells reload, only when it differs.
    public void WriteEnvScript()
    {
        WriteEnvScript(OperatingSystem.IsWindows() ? Shell.Cmd : Shell.Bash);
    }

    private string WriteEnvScript(Shell shell)
    {
        EnsureScanned();

        var (paths, envs) = BuildEnv();
        var fileName = PathUtils.GetCachePath(ShellScripts.ScriptFileName(shell));

        // cmd reads a batch file in the code page of the console, the shells of other systems in UTF-8.
        var encoding = shell == Shell.Cmd && Console.OutputEncoding is not UTF8Encoding ? Console.OutputEncoding : new UTF8Encoding(false);

        var state = File.Exists(PathUtils.StateFile) ? File.ReadAllText(PathUtils.StateFile).Trim() : "";
        var script = ShellScripts.Script(shell, paths, envs, state);
        if (File.Exists(fileName) && File.ReadAllText(fileName, encoding) == script)
            return script;

        state = DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);
        script = ShellScripts.Script(shell, paths, envs, state);

        // A console may read the script at any time, so it is replaced in one step, and before the state, so a console
        // that sees the new state finds the new script.
        Directory.CreateDirectory(Path.GetDirectoryName(fileName)!);
        var temporary = fileName + ".tmp";
        File.WriteAllText(temporary, script, encoding);
        File.Move(temporary, fileName, overwrite: true);
        File.WriteAllText(PathUtils.StateFile, state);
        return script;
    }

    private (List<string> Paths, IReadOnlyList<KeyValuePair<string, string>> Envs) BuildEnv()
    {
        var pathBuilder = new PathBuilder();
        var envsBuilder = new EnvsBuilder();

        foreach (var version in GetTools())
        {
            pathBuilder.AddApplication(version);
            envsBuilder.AddApplication(version);
        }

        // devtools itself (and dt.cmd next to it) stays on PATH after the tools.
        return (pathBuilder.Values.Append(PathUtils.AssemblyDirectory).ToList(), envsBuilder.Values);
    }

    // Set by the scan of this command, commands that change the tools directory scan again.
    private bool scanned;

    public void Scan()
    {
        toolDefinitionProvider.Scan(PathUtils.GetRootedPath(settings.RootPath));
        scanned = true;
        RecordSources();
    }

    // config/apps.json keeps the repository of every application fetched from one, so `app restore` can install it again.
    // An application whose .app is no longer from a repository, such as one replaced by hand, is no longer restored.
    // One that is gone from the tools directory keeps its entry, that is what `app restore` is for.
    private void RecordSources()
    {
        foreach (var name in toolDefinitionProvider.GetNames())
        {
            var source = toolDefinitionProvider.GetSource(name);
            if (toolSettingsProvider.GetSettings(name).Source == source)
                continue;

            toolSettingsProvider.UpdateSettings(name, s =>
            {
                s.Source = source;
                s.Version ??= MetaVersion.Latest;
            });
        }
    }

    // Installs the applications from repositories in config/apps.json that have no installed version, such as after
    // the tools directory was lost. The selected version when it is an exact one, otherwise the newest.
    public void Restore(bool refresh = false)
    {
        Scan();

        var names = toolSettingsProvider.GetApplications()
            .Where(i => toolSettingsProvider.GetSettings(i).Source != null && !toolDefinitionProvider.GetVersions(i).Any())
            .Order(StringComparer.Ordinal)
            .ToList();

        if (names.Count == 0)
        {
            Console.WriteLine("Every application from a repository in config/apps.json is installed");
            return;
        }

        var failed = new List<string>();
        foreach (var name in names)
        {
            var toolSettings = toolSettingsProvider.GetSettings(name);
            var version = toolSettings.Version != null && !MetaVersion.IsMeta(toolSettings.Version) ? toolSettings.Version : MetaVersion.Latest;

            try
            {
                Install(name, version, refresh, toolSettings.Source);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"{name}: {e.Message}");
                failed.Add(name);
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Restored {names.Count - failed.Count}, failed {failed.Count} of {names.Count} application(s)");

        if (failed.Count > 0)
            throw new InvalidOperationException($"Could not restore {failed.Join(", ")}");
    }

    public void Reset(string application, string source = null)
    {
        if (sources == null)
            throw new InvalidOperationException("No manifest repositories");

        sources.Value.Reset(application, source);
        Scan();
    }

    // The tools directory is scanned once in every command, so changes made by hand are found right away.
    private void EnsureScanned()
    {
        if (!scanned)
            Scan();
    }

    public void Prune()
    {
        Scan();

        var pruned = 0;

        foreach (var application in toolSettingsProvider.GetApplications())
        {
            var versions = toolDefinitionProvider.GetVersions(application);

            // An application from a repository stays for `app restore`.
            if (!versions.Any() && toolSettingsProvider.GetSettings(application).Source != null)
                continue;

            if (!versions.Any())
            {
                toolSettingsProvider.RemoveSettings(application);
                Console.WriteLine($"Removed settings of {application}: {(IsKnown(application) ? "no installed versions" : "application not found")}");
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

    public void Install(string application, string version, bool refresh = false, string source = null)
    {
        Scan();

        var directory = FindOrFetchSourceDirectory(application, source);
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

    // Every application with an install section, one that fails does not stop the others.
    public void UpdateAll(bool refresh = false)
    {
        Scan();

        var names = toolDefinitionProvider.GetNames().Where(i => toolDefinitionProvider.GetVersions(i).Any() && HasSource(i)).ToList();
        if (names.Count == 0)
        {
            Console.WriteLine("No application has an install section, `app install` gets one from the sources");
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
        var manifest = toolDefinitionProvider.GetManifest(PathUtils.GetRootedPath(settings.RootPath), application);
        return manifest is { Flat: false, Install: not null };
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

        if (definition.Manifest.Flat)
            throw new InvalidOperationException($"{application} in `{definition.Path}` is flat, it has no versions to remove");

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

    // Manifests from the sources are brought up to date first, an application without them gets them.
    private string FindOrFetchSourceDirectory(string application, string source = null)
    {
        if (sources != null && sources.Value.Refresh(application, source))
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
        var table = new TextTable("NAME", "SELECTED", "INSTALLED", "SOURCE", "STATUS");

        // Applications from repositories that are not in the tools directory are listed too, `app restore` installs them.
        var configured = toolSettingsProvider.GetApplications().Where(i => all || toolSettingsProvider.GetSettings(i).Source != null);
        var unmanaged = toolDefinitionProvider.GetUnmanaged();
        var names = toolDefinitionProvider.GetNames().Union(configured).Union(unmanaged).Order(StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var name in names)
        {
            var toolSettings = toolSettingsProvider.GetSettings(name);

            if (!IsKnown(name) && unmanaged.Contains(name))
                table.AddRow(name, "-", "-", "-", "unmanaged");
            else if (IsFlat(name))
                table.AddRow(name, "-", "-", DescribeKind(name, toolSettings), GetStatus(name, toolSettings));
            else
                table.AddRow(name, FormatVersion(name, toolSettings.Version), SummarizeVersions(name), DescribeKind(name, toolSettings), GetStatus(name, toolSettings));
        }

        table.Write();
    }

    // repo for an application from a repository, local for one with .app made by hand.
    private string DescribeKind(string application, ToolSettings toolSettings)
    {
        if (IsKnown(application))
            return toolDefinitionProvider.GetSource(application) != null ? "repo" : "local";

        return toolSettings.Source != null ? "repo" : "-";
    }

    // Many versions would make the column too wide, `ls <application>` lists all of them.
    private string SummarizeVersions(string application)
    {
        var versions = toolDefinitionProvider.GetVersions(application).Select(i => i.Manifest.Version).ToList();
        return versions.Count <= 3 ? versions.Join(", ") : $"{versions[0]} - {versions[^1]} ({versions.Count} versions)";
    }

    public void Show(string application)
    {
        EnsureScanned();
        if (!IsKnown(application))
        {
            ShowUnknown(application);
            return;
        }

        var versions = toolDefinitionProvider.GetVersions(application);
        var toolSettings = toolSettingsProvider.GetSettings(application);
        var selected = toolSettings.Version != null ? toolDefinitionProvider.GetVersion(application, toolSettings.Version) : null;
        var versioned = !IsFlat(application);
        var directory = versions.Count == 0
            ? toolDefinitionProvider.FindDirectory(PathUtils.GetRootedPath(settings.RootPath), application)
            : versioned ? Path.GetDirectoryName(versions[0].Path) : versions[0].Path;

        var rows = new List<(string Name, string Value)>
        {
            ("status", GetStatus(application, toolSettings) is { Length: > 0 } status ? status : "enabled"),
            ("directory", directory)
        };

        if (versioned)
        {
            rows.Add(("selected", FormatVersion(application, toolSettings.Version)));
            rows.Add(("versions", versions.Count > 0 ? versions.Select(i => i.Manifest.Version).Reverse().Join(", ") : $"none, `app install {application}` installs one"));
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

    // An application only in config/apps.json or a directory without .app.
    private void ShowUnknown(string application)
    {
        var source = toolSettingsProvider.GetApplications().Contains(application) ? toolSettingsProvider.GetSettings(application).Source : null;

        if (source != null)
        {
            Console.WriteLine(application);
            Console.WriteLine($"  {"status",-10} not installed, `app restore` installs it");
            Console.WriteLine($"  {"source",-10} {source}");
            return;
        }

        if (!toolDefinitionProvider.GetUnmanaged().Contains(application))
            throw NotFound(application);

        Console.WriteLine(application);
        Console.WriteLine($"  {"status",-10} unmanaged, the directory has no {ToolManifest.FileName}");
        Console.WriteLine($"  {"directory",-10} {Path.GetFullPath(Path.Combine(PathUtils.GetRootedPath(settings.RootPath), application))}");
    }

    private string DescribeSource(string application, string directory)
    {
        var manifest = toolDefinitionProvider.GetManifest(PathUtils.GetRootedPath(settings.RootPath), application);

        if (manifest?.Install == null)
            return "none, versions are added by hand";

        if (manifest.Origin == null)
            return $"local {ToolManifest.FileName}";

        return manifest.Origin.Repository + (ManifestHash.IsModified(Path.Combine(directory, ToolManifest.FileName)) ? ", changed locally" : "");
    }

    public void Select(string application, string version)
    {
        var installed = FindVersions(application);

        if (IsFlat(application))
        {
            Console.WriteLine($"{application} is flat, there is no version to select");
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

        if (versions.Count > 0)
            return versions;

        if (IsKnown(application))
            throw new InvalidOperationException($"{application} has no installed versions, `app install {application}` installs one");

        throw NotFound(application);
    }

    private bool IsKnown(string application)
    {
        return toolDefinitionProvider.GetNames().Contains(application);
    }

    private static InvalidOperationException NotFound(string application)
    {
        return new InvalidOperationException($"{application} not found, `ls` lists the applications");
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

    private bool IsFlat(string application)
    {
        var versions = toolDefinitionProvider.GetVersions(application);
        return versions.Count == 1 && versions[0].Manifest.Version == MetaVersion.Default;
    }

    private string GetStatus(string application, ToolSettings toolSettings)
    {
        if (!toolDefinitionProvider.GetVersions(application).Any())
            return IsKnown(application) || toolSettings.Source != null ? "not installed" : "orphaned";

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