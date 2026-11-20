using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DevTools.Infrastructure;

namespace DevTools.Management;

// Every shell works the same way: after every change devtools writes cache/env.cmd or cache/env.sh, which sets PATH
// and variables, and the shell runs it without starting devtools. `shell env` writes it again when it is missing
// and prints the same commands for hooks of earlier versions. The logic stays here, so the shells only differ in syntax.
public static partial class ShellScripts
{
    public const string BasePathVariable = "DT_BASEPATH";
    public const string EnvsVariable = "DT_ENVS";
    public const string InitializedVariable = "DT_INITIALIZED";

    // The stamp of cache/state the shell last loaded, `dt` reloads only when the file has another one.
    public const string StateVariable = "DT_STATE";

    // Windows only has cmd integration. Elsewhere $SHELL is the login shell of the user, the one their terminals start.
    // The parent process is not used, it is sudo, an IDE or a script as often as the shell.
    public static Shell Detect(bool isWindows, Func<string, string> getEnv)
    {
        if (isWindows)
            return Shell.Cmd;

        var shell = getEnv("SHELL");
        return Path.GetFileName(shell ?? "") switch
        {
            "bash" => Shell.Bash,
            "zsh" => Shell.Zsh,
            _ => throw new InvalidOperationException($"Cannot detect the shell from SHELL=`{shell}`, pass cmd, bash or zsh")
        };
    }

    // The PATH from before devtools is kept in DT_BASEPATH, so every reload builds PATH from scratch.
    // Selected tools go first, so they win over the system ones.
    public static string Env(Shell shell, IEnumerable<string> paths, IReadOnlyList<KeyValuePair<string, string>> envs, Func<string, string> getEnv, string state = "")
    {
        var separator = shell == Shell.Cmd ? ';' : ':';
        var basePath = new[] { getEnv(BasePathVariable), getEnv("PATH") }.FirstOrDefault(i => !string.IsNullOrEmpty(i)) ?? "";
        var toolPath = paths.Where(i => !string.IsNullOrEmpty(i)).Join(separator);

        foreach (var (key, _) in envs.Where(i => !IsValid(shell, i)))
            Console.Error.WriteLine($"Skipped environment variable `{key}`: its name or value cannot be set in {shell}");

        var valid = envs.Where(i => IsValid(shell, i)).ToList();
        var comparer = shell == Shell.Cmd ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var names = valid.Select(i => i.Key).Distinct(comparer).ToList();
        var stale = (getEnv(EnvsVariable) ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(i => IsName(shell, i) && !names.Contains(i, comparer))
            .Distinct(comparer);

        var lines = new List<string> { Set(shell, BasePathVariable, basePath) };
        lines.AddRange(stale.Select(i => Unset(shell, i)));
        lines.AddRange(valid.Select(i => Set(shell, i.Key, i.Value)));
        lines.Add(Set(shell, EnvsVariable, names.Join(' ')));
        lines.Add(Set(shell, "DT_PATH", toolPath));
        lines.Add(Set(shell, "PATH", new[] { toolPath, basePath }.Where(i => i.Length > 0).Join(separator)));
        lines.Add(Set(shell, StateVariable, state));
        lines.Add(Set(shell, InitializedVariable, "1"));

        return lines.Join(shell == Shell.Cmd ? "\r\n" : "\n");
    }

    // The script of Env for the file in cache. What Env reads from the environment is read by the shell instead, so one file
    // serves every console: PATH from before devtools and the variables the console set before, which are removed first.
    public static string Script(Shell shell, IEnumerable<string> paths, IReadOnlyList<KeyValuePair<string, string>> envs, string state)
    {
        var separator = shell == Shell.Cmd ? ';' : ':';
        var toolPath = paths.Where(i => !string.IsNullOrEmpty(i)).Join(separator);

        foreach (var (key, _) in envs.Where(i => !IsValid(shell, i)))
            Console.Error.WriteLine($"Skipped environment variable `{key}`: its name or value cannot be set in {shell}");

        var valid = envs.Where(i => IsValid(shell, i)).ToList();
        var comparer = shell == Shell.Cmd ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var names = valid.Select(i => i.Key).Distinct(comparer).Join(' ');

        if (shell == Shell.Cmd)
        {
            // Every line starts with @, so an interactive console does not echo them. % is doubled in a batch file.
            var lines = new List<string>
            {
                $"@if not defined {BasePathVariable} set \"{BasePathVariable}=%PATH%\"",
                $"@call :unset %{EnvsVariable}%"
            };
            lines.AddRange(valid.Select(i => "@" + Set(shell, i.Key, Batch(i.Value))));
            lines.Add("@" + Set(shell, EnvsVariable, names));
            lines.Add("@" + Set(shell, "DT_PATH", Batch(toolPath)));
            lines.Add("@" + Set(shell, "PATH", toolPath.Length > 0 ? $"{Batch(toolPath)};%{BasePathVariable}%" : $"%{BasePathVariable}%"));
            lines.Add("@" + Set(shell, StateVariable, state));
            lines.Add("@" + Set(shell, InitializedVariable, "1"));
            lines.Add("@goto :eof");
            // call splits the names at spaces, a for loop would stop at a name such as ProgramFiles(x86).
            lines.Add(":unset");
            lines.Add("@if \"%~1\"==\"\" goto :eof");
            lines.Add("@set \"%~1=\"");
            lines.Add("@shift");
            lines.Add("@goto unset");
            return lines.Join("\r\n") + "\r\n";
        }

        var posix = new List<string>
        {
            $"export {BasePathVariable}=\"${{{BasePathVariable}:-$PATH}}\"",
            $"[ -n \"${{{EnvsVariable}-}}\" ] && eval \"unset ${EnvsVariable}\" 2>/dev/null"
        };
        posix.AddRange(valid.Select(i => Set(shell, i.Key, i.Value)));
        posix.Add(Set(shell, EnvsVariable, names));
        posix.Add(Set(shell, "DT_PATH", toolPath));
        posix.Add(toolPath.Length > 0 ? $"export PATH={Quote(toolPath)}\"${{{BasePathVariable}:+:${BasePathVariable}}}\"" : $"export PATH=\"${BasePathVariable}\"");
        posix.Add(Set(shell, StateVariable, state));
        posix.Add(Set(shell, InitializedVariable, "1"));
        return posix.Join("\n") + "\n";
    }

    // The scripts of bash and zsh are the same.
    public static string ScriptFileName(Shell shell)
    {
        return shell == Shell.Cmd ? "env.cmd" : "env.sh";
    }

    public static string ScriptFile(Shell shell, string executable)
    {
        return Path.Combine(Path.GetDirectoryName(executable) ?? "", PathUtils.CacheDirectoryName, ScriptFileName(shell));
    }

    // cmd: the AutoRun command, it runs env.cmd and lets devtools write it first when it is missing.
    // bash and zsh: sourced by `eval "$(devtools shell hook bash)"`, a function is needed because a child process cannot change the shell.
    // A script older than the state, such as after devtools was stopped right after a save, is written again too.
    public static string Hook(Shell shell, string executable)
    {
        var script = ScriptFile(shell, executable);

        if (shell == Shell.Cmd)
            return $"""if not defined {InitializedVariable} (set "{InitializedVariable}=1" & (if not exist "{script}" "{executable}" shell env cmd >nul) & call "{script}")""";

        var exe = Quote(executable);
        var name = Name(shell);
        var stateFile = Quote(StateFile(executable));
        var scriptFile = Quote(script);

        return $$"""
            _dt_reload() {
              local state=
              [ -r {{stateFile}} ] && IFS= read -r state < {{stateFile}}
              [ -r {{scriptFile}} ] && . {{scriptFile}}
              [ -r {{scriptFile}} ] && [ "$state" = "${{{StateVariable}}-}" ] && return
              {{exe}} shell env {{name}} >/dev/null && . {{scriptFile}}
            }

            dt() {
              {{exe}} "$@"
              local result=$? state=
              [ -r {{stateFile}} ] && IFS= read -r state < {{stateFile}}
              [ "$state" = "${{{StateVariable}}-}" ] || _dt_reload
              return $result
            }

            _dt_reload
            """.ReplaceLineEndings("\n");
    }

    public static string StateFile(string executable)
    {
        return Path.Combine(Path.GetDirectoryName(executable) ?? "", PathUtils.CacheDirectoryName, PathUtils.StateFileName);
    }

    public static string RcLine(Shell shell, string executable)
    {
        return $"eval \"$({Quote(executable)} shell hook {Name(shell)})\"";
    }

    public static string RcFile(Shell shell, Func<string, string> getEnv)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return shell switch
        {
            Shell.Bash => Path.Combine(home, ".bashrc"),
            Shell.Zsh => Path.Combine(getEnv("ZDOTDIR") ?? home, ".zshrc"),
            _ => throw new ArgumentOutOfRangeException(nameof(shell))
        };
    }

    // devtools always goes last in AutoRun, so the commands of other tools stay as they were.
    public static string MergeAutoRun(string existing, string command)
    {
        var others = RemoveFromAutoRun(existing);
        return others.Length == 0 ? command : $"{others} & {command}";
    }

    // What is left of AutoRun without devtools, also without the dt-setup.cmd of older versions.
    public static string RemoveFromAutoRun(string existing)
    {
        if (string.IsNullOrWhiteSpace(existing))
            return "";

        var start = existing.IndexOf($"if not defined {InitializedVariable}", StringComparison.OrdinalIgnoreCase);
        var others = start >= 0 ? existing[..start] : existing;

        if (others.Contains("dt-setup.cmd", StringComparison.OrdinalIgnoreCase))
            others = others.Split('&').Where(i => !i.Contains("dt-setup.cmd", StringComparison.OrdinalIgnoreCase)).Select(i => i.Trim()).Join(" & ");

        return others.Trim().Trim('&').Trim();
    }

    // Removes the lines added by `shell install`, including ones that point to another devtools location
    // and the `init` lines of earlier versions.
    public static string RemoveFromRc(string content, Shell shell)
    {
        var lineEnding = content.Contains("\r\n") ? "\r\n" : "\n";
        var rcLine = new Regex($"""^\s*eval "\$\(.*devtools.*(hook|init) {Name(shell)}\)"\s*$""");
        var kept = new List<string>();

        foreach (var line in content.Split(lineEnding))
        {
            if (line.Trim() == RcMarker)
            {
                if (kept.Count > 0 && kept[^1].Trim().Length == 0)
                    kept.RemoveAt(kept.Count - 1);
                continue;
            }

            if (!rcLine.IsMatch(line))
                kept.Add(line);
        }

        return kept.Join(lineEnding);
    }

    public const string RcMarker = "# devtools";

    private static string Name(Shell shell)
    {
        return shell.ToString().ToLowerInvariant();
    }

    private static string Set(Shell shell, string key, string value)
    {
        return shell == Shell.Cmd ? $"set \"{key}={value}\"" : $"export {key}={Quote(value)}";
    }

    private static string Unset(Shell shell, string key)
    {
        return shell == Shell.Cmd ? $"set \"{key}=\"" : $"unset {key}";
    }

    private static string Batch(string value)
    {
        return value.Replace("%", "%%");
    }

    private static string Quote(string value)
    {
        return "'" + value.Replace("'", @"'\''") + "'";
    }

    // In cmd a quote or a line break would end the set command, so such values are not written.
    private static bool IsValid(Shell shell, KeyValuePair<string, string> env)
    {
        return IsName(shell, env.Key) && (shell != Shell.Cmd || env.Value.IndexOfAny(new[] { '"', '\r', '\n' }) < 0);
    }

    private static bool IsName(Shell shell, string name)
    {
        return shell == Shell.Cmd
            ? CmdName().IsMatch(name)
            : PosixName().IsMatch(name);
    }

    [GeneratedRegex("""^[^\s="%!^&|<>]+$""")]
    private static partial Regex CmdName();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex PosixName();
}
