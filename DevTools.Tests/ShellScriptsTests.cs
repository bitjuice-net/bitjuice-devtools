using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using DevTools.Management;
using Xunit;

namespace DevTools.Tests;

public class ShellScriptsTests
{
    private static readonly KeyValuePair<string, string>[] Envs =
    {
        new("JAVA_HOME", "/tools/java/25"),
        new("GREETING", "it's $HOME `x`")
    };

    private static Func<string, string> Env(params (string Key, string Value)[] values)
    {
        var dictionary = new Dictionary<string, string>();
        foreach (var (key, value) in values)
            dictionary[key] = value;
        return key => dictionary.GetValueOrDefault(key);
    }

    [Theory]
    [InlineData("/bin/bash", Shell.Bash)]
    [InlineData("/usr/local/bin/zsh", Shell.Zsh)]
    [InlineData("zsh", Shell.Zsh)]
    public void Detect_UsesShellVariableOutsideWindows(string shell, Shell expected)
    {
        Assert.Equal(expected, ShellScripts.Detect(false, Env(("SHELL", shell))));
    }

    [Fact]
    public void Detect_IsCmdOnWindows()
    {
        Assert.Equal(Shell.Cmd, ShellScripts.Detect(true, Env(("SHELL", "/usr/bin/bash"))));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("/usr/bin/fish")]
    public void Detect_FailsForUnsupportedShell(string shell)
    {
        var error = Assert.Throws<InvalidOperationException>(() => ShellScripts.Detect(false, Env(("SHELL", shell))));

        Assert.Contains("pass cmd, bash or zsh", error.Message);
    }

    [Theory]
    [InlineData(Shell.Bash)]
    [InlineData(Shell.Zsh)]
    public void Env_PosixShellsExportQuotedValuesAndPutToolsFirst(Shell shell)
    {
        var setup = ShellScripts.Env(shell, new[] { "/a", "/b" }, Envs, Env(("PATH", "/usr/bin")));

        Assert.Equal(new[]
        {
            "export DT_BASEPATH='/usr/bin'",
            "export JAVA_HOME='/tools/java/25'",
            @"export GREETING='it'\''s $HOME `x`'",
            "export DT_ENVS='JAVA_HOME GREETING'",
            "export DT_PATH='/a:/b'",
            "export PATH='/a:/b:/usr/bin'",
            "export DT_STATE=''",
            "export DT_INITIALIZED='1'"
        }, setup.Split('\n'));
    }

    [Fact]
    public void Env_CmdSetsQuotedAssignments()
    {
        var setup = ShellScripts.Env(Shell.Cmd, new[] { @"D:\a", @"D:\b" }, new KeyValuePair<string, string>[] { new("ProgramFiles(x86)", "x & y") }, Env(("PATH", @"C:\Windows")));

        Assert.Equal(new[]
        {
            @"set ""DT_BASEPATH=C:\Windows""",
            @"set ""ProgramFiles(x86)=x & y""",
            @"set ""DT_ENVS=ProgramFiles(x86)""",
            @"set ""DT_PATH=D:\a;D:\b""",
            @"set ""PATH=D:\a;D:\b;C:\Windows""",
            @"set ""DT_STATE=""",
            @"set ""DT_INITIALIZED=1"""
        }, setup.Split("\r\n"));
    }

    [Theory]
    [InlineData(Shell.Cmd)]
    [InlineData(Shell.Bash)]
    public void Env_RebuildsPathFromBasePath(Shell shell)
    {
        var setup = ShellScripts.Env(shell, new[] { "new" }, Array.Empty<KeyValuePair<string, string>>(), Env(("PATH", "old;base"), ("DT_BASEPATH", "base")));

        Assert.Contains(shell == Shell.Cmd ? @"set ""PATH=new;base""" : "export PATH='new:base'", setup);
        Assert.DoesNotContain("old", setup);
    }

    [Fact]
    public void Env_EmptyBasePathFallsBackToPath()
    {
        var setup = ShellScripts.Env(Shell.Bash, new[] { "/a" }, Array.Empty<KeyValuePair<string, string>>(), Env(("PATH", "/usr/bin"), ("DT_BASEPATH", "")));

        Assert.Contains("export PATH='/a:/usr/bin'", setup);
    }

    [Theory]
    [InlineData(Shell.Cmd, @"set ""OLD=""")]
    [InlineData(Shell.Bash, "unset OLD")]
    public void Env_UnsetsVariablesNoLongerSet(Shell shell, string unset)
    {
        var setup = ShellScripts.Env(shell, Array.Empty<string>(), Envs, Env(("PATH", "p"), ("DT_ENVS", "OLD JAVA_HOME")));

        Assert.Contains(unset, setup);
        Assert.DoesNotContain("JAVA_HOME=\"", setup);
        Assert.DoesNotContain("unset JAVA_HOME", setup);
    }

    [Theory]
    [InlineData(Shell.Bash, "ProgramFiles(x86)", "x")]
    [InlineData(Shell.Cmd, "QUOTED", "a\"b")]
    [InlineData(Shell.Cmd, "MULTILINE", "a\nb")]
    public void Env_SkipsVariablesTheShellCannotSet(Shell shell, string key, string value)
    {
        var output = TestTools.CaptureError(() =>
        {
            var setup = ShellScripts.Env(shell, Array.Empty<string>(), new KeyValuePair<string, string>[] { new(key, value), new("OK", "y") }, Env());

            Assert.DoesNotContain(key, setup);
            Assert.Contains("OK", setup);
        });

        Assert.Contains(key, output);
    }

    [Theory]
    [InlineData(Shell.Bash)]
    [InlineData(Shell.Zsh)]
    public void Hook_PosixShellsSourceInitSh(Shell shell)
    {
        var line = ShellScripts.Hook(shell, "/opt/dev tools/devtools");

        // The path comes from the system the tests run on, Windows turns the slashes around.
        Assert.Equal($". '{ShellScripts.InitFile(shell, "/opt/dev tools/devtools")}'", line);
        Assert.Equal(line, ShellScripts.RcLine(shell, "/opt/dev tools/devtools"));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(". '/opt/dev tools/init.sh'", line);
    }

    [Fact]
    public void Hook_CmdCallsInitCmdOnce()
    {
        const string exe = @"C:\Program Files\devtools\devtools.exe";

        // The path comes from the system the tests run on, a Windows path has no directory on Linux.
        var file = ShellScripts.InitFile(Shell.Cmd, exe);
        Assert.Equal($"if not defined DT_INITIALIZED call \"{file}\"", ShellScripts.Hook(Shell.Cmd, exe));
        if (OperatingSystem.IsWindows())
            Assert.Equal(@"C:\Program Files\devtools\init.cmd", file);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"D:\Tools\devtools\Scripts\dt-setup.cmd")]
    [InlineData("if not defined DT_INITIALIZED (old)")]
    public void MergeAutoRun_ReplacesEmptyOrDevtoolsEntry(string existing)
    {
        Assert.Equal("new", ShellScripts.MergeAutoRun(existing, "new"));
    }

    [Theory]
    [InlineData("clink inject")]
    [InlineData("clink inject & if not defined DT_INITIALIZED (old & stuff)")]
    [InlineData(@"clink inject & D:\Tools\devtools\Scripts\dt-setup.cmd")]
    public void MergeAutoRun_KeepsOtherCommandsAndPutsDevtoolsLast(string existing)
    {
        Assert.Equal("clink inject & new", ShellScripts.MergeAutoRun(existing, "new"));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("""if not defined DT_INITIALIZED (set "DT_INITIALIZED=1" & for /f "delims=" %i in ('x') do @%i)""", "")]
    [InlineData("""clink inject & if not defined DT_INITIALIZED (set "DT_INITIALIZED=1" & for /f "delims=" %i in ('x') do @%i)""", "clink inject")]
    [InlineData(@"D:\Tools\devtools\Scripts\dt-setup.cmd", "")]
    [InlineData(@"a & D:\Tools\devtools\Scripts\dt-setup.cmd & b", "a & b")]
    [InlineData("clink inject", "clink inject")]
    public void RemoveFromAutoRun_LeavesOtherCommands(string existing, string expected)
    {
        Assert.Equal(expected, ShellScripts.RemoveFromAutoRun(existing));
    }

    [Theory]
    [InlineData(Shell.Bash, "alias ll='ls -l'\n\n# devtools\neval \"$('/opt/devtools/devtools' init bash)\"\nexport X=1\n", "alias ll='ls -l'\nexport X=1\n")]
    [InlineData(Shell.Bash, "a\r\neval \"$('/old place/devtools' init bash)\"\r\n", "a\r\n")]
    [InlineData(Shell.Zsh, "eval \"$('/opt/devtools/devtools' init bash)\"\n", "eval \"$('/opt/devtools/devtools' init bash)\"\n")]
    [InlineData(Shell.Bash, "eval \"$(starship init bash)\"\n", "eval \"$(starship init bash)\"\n")]
    [InlineData(Shell.Zsh, "a\n. '/opt/devtools/init.sh'\nb\n", "a\nb\n")]
    [InlineData(Shell.Bash, ". '/home/me/.local/init.sh'\n", ". '/home/me/.local/init.sh'\n")]
    public void RemoveFromRc_RemovesOnlyDevtoolsLines(Shell shell, string content, string expected)
    {
        Assert.Equal(expected, ShellScripts.RemoveFromRc(content, shell));
    }

    [Fact]
    public void RemoveFromRc_UndoesInstall()
    {
        var original = "alias ll='ls -l'";
        var installed = ShellScripts.AppendToRc(original, ShellScripts.RcLine(Shell.Bash, "/opt/devtools/devtools"));

        Assert.Equal($"alias ll='ls -l'\n. '{ShellScripts.InitFile(Shell.Bash, "/opt/devtools/devtools")}'\n", installed);
        Assert.Equal(original + "\n", ShellScripts.RemoveFromRc(installed, Shell.Bash));
    }

    [Theory]
    [InlineData("", ". 'x'\n")]
    [InlineData("a", "a\n. 'x'\n")]
    [InlineData("a\r\n", "a\r\n. 'x'\r\n")]
    public void AppendToRc_AddsOneLine(string content, string expected)
    {
        Assert.Equal(expected, ShellScripts.AppendToRc(content, ". 'x'"));
    }

    [Fact]
    public void Env_SetsTheLoadedState()
    {
        var env = ShellScripts.Env(Shell.Bash, new[] { "/a" }, Array.Empty<KeyValuePair<string, string>>(), Env(("PATH", "/usr/bin")), "42");

        Assert.Contains("export DT_STATE='42'", env);
    }

    [Fact]
    public void Script_PosixReadsBasePathAndOldVariablesInTheShell()
    {
        var script = ShellScripts.Script(Shell.Bash, new[] { "/a", "/b" }, Envs, "42");

        Assert.Contains("export DT_BASEPATH=\"${DT_BASEPATH:-$PATH}\"", script);
        Assert.Contains("[ -n \"${DT_ENVS-}\" ] && eval \"unset $DT_ENVS\"", script);
        Assert.Contains("export GREETING='it'\\''s $HOME `x`'", script);
        Assert.Contains("export DT_ENVS='JAVA_HOME GREETING'", script);
        Assert.Contains("export PATH='/a:/b'\"${DT_BASEPATH:+:$DT_BASEPATH}\"", script);
        Assert.Contains("export DT_STATE='42'", script);
        Assert.DoesNotContain("\r", script);
    }

    [Fact]
    public void Script_CmdDoublesPercentAndHidesEveryLine()
    {
        var script = ShellScripts.Script(Shell.Cmd, new[] { @"D:\a%b" }, new KeyValuePair<string, string>[] { new("X", "100% & more") }, "42");
        var lines = script.TrimEnd().Split("\r\n");

        Assert.Contains("""@set "X=100%% & more" """.TrimEnd(), lines);
        Assert.Contains("""@set "PATH=D:\a%%b;%DT_BASEPATH%" """.TrimEnd(), lines);
        Assert.All(lines, i => Assert.True(i.StartsWith('@') || i.StartsWith(':'), i));
    }

    // Runs the script in cmd twice, like a console after two changes: the second one removes what the first set.
    [Fact]
    public void Script_CmdSetsPathAndVariablesAndRemovesOldOnes()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var directory = Directory.CreateTempSubdirectory("devtools-env-").FullName;
        try
        {
            var first = Path.Combine(directory, "first.cmd");
            var second = Path.Combine(directory, "second.cmd");
            File.WriteAllText(first, ShellScripts.Script(Shell.Cmd, new[] { @"D:\one" },
                new KeyValuePair<string, string>[] { new("ProgramFiles(x86)", "x"), new("DT_OLD", "1") }, "1"));
            File.WriteAllText(second, ShellScripts.Script(Shell.Cmd, new[] { @"D:\two" },
                new KeyValuePair<string, string>[] { new("DT_NEW", "50% & (y)") }, "2"));
            var test = Path.Combine(directory, "test.cmd");
            File.WriteAllText(test, $"""
                @echo off
                set "DT_BASEPATH=" & set "DT_ENVS="
                set "PATH=C:\base"
                call "{first}"
                call "{second}"
                echo [%PATH%]
                echo ["%DT_NEW%"]
                echo [%DT_OLD%][%ProgramFiles(x86)%][%DT_STATE%][%DT_BASEPATH%]
                """.ReplaceLineEndings("\r\n"));

            var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c \"{test}\"") { RedirectStandardOutput = true, UseShellExecute = false })!;
            var output = process.StandardOutput.ReadToEnd().Split("\r\n");
            process.WaitForExit();

            Assert.Equal(@"[D:\two;C:\base]", output[0]);
            Assert.Equal("[\"50% & (y)\"]", output[1]);
            Assert.Equal(@"[][][2][C:\base]", output[2]);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    // Runs init.cmd of the build output like AutoRun does, with an env.cmd of the current state.
    [Fact]
    public void InitCmd_LoadsEnvCmd()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var directory = Directory.CreateTempSubdirectory("devtools-init-").FullName;
        try
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "init.cmd"), Path.Combine(directory, "init.cmd"));
            Directory.CreateDirectory(Path.Combine(directory, "cache"));
            File.WriteAllText(Path.Combine(directory, "cache", "state"), "7\r\n");
            File.WriteAllText(Path.Combine(directory, "cache", "env.cmd"), ShellScripts.Script(Shell.Cmd, new[] { @"D:\one" },
                new KeyValuePair<string, string>[] { new("DT_X", "x") }, "7"));
            var test = Path.Combine(directory, "test.cmd");
            File.WriteAllText(test, $"""
                @echo off
                set "DT_BASEPATH=" & set "DT_ENVS=" & set "DT_STATE=" & set "DT_INITIALIZED="
                set "PATH=C:\base"
                call "{directory}\init.cmd"
                echo [%PATH%][%DT_X%][%DT_STATE%][%DT_INITIALIZED%][%DT_NEWSTATE%]
                """.ReplaceLineEndings("\r\n"));

            var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c \"{test}\"") { RedirectStandardOutput = true, UseShellExecute = false })!;
            var output = process.StandardOutput.ReadToEnd().Split("\r\n");
            process.WaitForExit();

            Assert.Equal(@"[D:\one;C:\base][x][7][1][]", output[0]);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    // Sources init.sh of the build output in bash and zsh, where they are installed. A fake devtools writes env.sh
    // on `shell env` and changes the state on any other command, so dt has to reload.
    [Theory]
    [InlineData("bash")]
    [InlineData("zsh")]
    public void InitSh_LoadsEnvShAndDefinesDt(string shell)
    {
        if (OperatingSystem.IsWindows() || !new[] { "/bin", "/usr/bin" }.Any(i => File.Exists(Path.Combine(i, shell))))
            return;

        var directory = Directory.CreateTempSubdirectory("devtools-init-").FullName;
        try
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "init.sh"), Path.Combine(directory, "init.sh"));
            Directory.CreateDirectory(Path.Combine(directory, "cache"));
            File.WriteAllText(Path.Combine(directory, "env1.sh"),
                ShellScripts.Script(Shell.Bash, new[] { "/one" }, new KeyValuePair<string, string>[] { new("DT_X", "1") }, "1"));
            File.WriteAllText(Path.Combine(directory, "env2.sh"),
                ShellScripts.Script(Shell.Bash, new[] { "/two" }, new KeyValuePair<string, string>[] { new("DT_X", "2") }, "2"));
            File.WriteAllText(Path.Combine(directory, "cache", "state"), "1\n");

            var devtools = Path.Combine(directory, "devtools");
            File.WriteAllText(devtools, """
                #!/bin/sh
                dir=$(dirname "$0")
                if [ "$1" = shell ]; then cp "$dir/env$(cat "$dir/cache/state").sh" "$dir/cache/env.sh"; else echo 2 > "$dir/cache/state"; fi
                """.ReplaceLineEndings("\n") + "\n");
            File.SetUnixFileMode(devtools, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var script = $"""
                PATH=/usr/bin:/bin
                . '{directory}/init.sh'
                echo "[$PATH][$DT_X][$DT_STATE]"
                dt sl x 2
                echo "[$PATH][$DT_X][$DT_STATE]"
                """.ReplaceLineEndings("\n");
            var info = new ProcessStartInfo(shell) { RedirectStandardOutput = true, UseShellExecute = false };
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add(script);
            info.Environment.Remove("DT_BASEPATH");
            info.Environment.Remove("DT_ENVS");
            info.Environment.Remove("DT_STATE");
            var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEnd().Split("\n");
            process.WaitForExit();

            Assert.Equal("[/one:/usr/bin:/bin][1][1]", output[0]);
            Assert.Equal("[/two:/usr/bin:/bin][2][2]", output[1]);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
