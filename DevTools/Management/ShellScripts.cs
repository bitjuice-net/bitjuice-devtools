using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DevTools.Infrastructure;

namespace DevTools.Management;

// Every shell works the same way: after every change devtools writes cache/env.cmd or cache/env.sh, which sets PATH
// and variables, and init.cmd or init.sh, which new consoles run, loads it without starting devtools. `shell env` writes
// it again when it is missing or outdated.
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

    // The script for the file in cache. PATH from before devtools, kept in DT_BASEPATH, and the variables the console set
    // before, which are removed first, are read by the shell, so one file serves every console. Selected tools go first
    // in PATH, so they win over the system ones.
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

    // init.cmd or init.sh next to devtools, what a new console runs. They load cache/env.cmd or cache/env.sh, let devtools
    // write it first when it is missing or older than the state, and init.sh defines `dt`.
    public static string InitFile(Shell shell, string executable)
    {
        return Path.Combine(Path.GetDirectoryName(executable) ?? "", shell == Shell.Cmd ? "init.cmd" : "init.sh");
    }

    // cmd: the AutoRun command. Consoles started by one that loaded devtools, such as `cmd /c` of scripts, skip it.
    // bash and zsh: the line of ~/.bashrc or ~/.zshrc.
    public static string Hook(Shell shell, string executable)
    {
        var file = InitFile(shell, executable);

        return shell == Shell.Cmd
            ? $"if not defined {InitializedVariable} call \"{file}\""
            : $". {Quote(file)}";
    }

    public static string RcLine(Shell shell, string executable)
    {
        return Hook(shell, executable);
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

    // What is left of AutoRun without devtools.
    public static string RemoveFromAutoRun(string existing)
    {
        if (string.IsNullOrWhiteSpace(existing))
            return "";

        var start = existing.IndexOf($"if not defined {InitializedVariable}", StringComparison.OrdinalIgnoreCase);
        var others = start >= 0 ? existing[..start] : existing;
        return others.Trim().Trim('&').Trim();
    }

    // Removes the line added by `shell install`, also one that points to another devtools location.
    public static string RemoveFromRc(string content)
    {
        var lineEnding = content.Contains("\r\n") ? "\r\n" : "\n";
        var rcLine = RcLinePattern();
        return content.Split(lineEnding).Where(i => !rcLine.IsMatch(i)).Join(lineEnding);
    }

    public static string AppendToRc(string content, string line)
    {
        var lineEnding = content.Contains("\r\n") ? "\r\n" : "\n";
        var separator = content.Length == 0 || content.EndsWith('\n') ? "" : lineEnding;
        return content + separator + line + lineEnding;
    }

    private static string Set(Shell shell, string key, string value)
    {
        return shell == Shell.Cmd ? $"set \"{key}={value}\"" : $"export {key}={Quote(value)}";
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

    [GeneratedRegex(@"^\s*\. '[^']*devtools[^']*[/\\]init\.sh'\s*$")]
    private static partial Regex RcLinePattern();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex PosixName();
}
