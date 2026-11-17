using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DevTools.Infrastructure;

namespace DevTools.Management;

// Every shell works the same way: `setup` prints commands that set PATH and variables, the shell runs them.
// The logic stays here, so the shells only differ in syntax.
public static class ShellScripts
{
    public const string BasePathVariable = "DT_BASEPATH";
    public const string EnvsVariable = "DT_ENVS";
    public const string InitializedVariable = "DT_INITIALIZED";

    public static Shell Default => OperatingSystem.IsWindows() ? Shell.Cmd : Shell.Bash;

    // The PATH from before devtools is kept in DT_BASEPATH, so every reload builds PATH from scratch.
    // Selected tools go first, so they win over the system ones.
    public static string Setup(Shell shell, IEnumerable<string> paths, IReadOnlyList<KeyValuePair<string, string>> envs, Func<string, string> getEnv)
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
        lines.Add(Set(shell, InitializedVariable, "1"));

        return lines.Join(shell == Shell.Cmd ? "\r\n" : "\n");
    }

    // cmd: the AutoRun command. DT_INITIALIZED goes first, because for /f runs its command in a nested cmd that runs AutoRun too.
    // bash and zsh: sourced by `eval "$(devtools shell init bash)"`, a function is needed because a child process cannot change the shell.
    public static string Init(Shell shell, string executable)
    {
        if (shell == Shell.Cmd)
            return $"""if not defined {InitializedVariable} (set "{InitializedVariable}=1" & for /f "delims=" %i in ('""{executable}" shell setup cmd"') do @%i)""";

        var exe = Quote(executable);
        var name = Name(shell);

        return $$"""
            _dt_reload() {
              local setup
              setup="$({{exe}} shell setup {{name}})" || return
              eval "$setup"
            }

            dt() {
              {{exe}} "$@"
              local result=$?
              _dt_reload
              return $result
            }

            _dt_reload
            """.ReplaceLineEndings("\n");
    }

    public static string RcLine(Shell shell, string executable)
    {
        return $"eval \"$({Quote(executable)} shell init {Name(shell)})\"";
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

    // Removes the lines added by `shell install`, including ones that point to another devtools location.
    public static string RemoveFromRc(string content, Shell shell)
    {
        var lineEnding = content.Contains("\r\n") ? "\r\n" : "\n";
        var rcLine = new Regex($"""^\s*eval "\$\(.*devtools.*init {Name(shell)}\)"\s*$""");
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
            ? Regex.IsMatch(name, """^[^\s="%!^&|<>]+$""")
            : Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$");
    }
}
