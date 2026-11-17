using System;
using System.Collections.Generic;
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
    [InlineData(Shell.Bash)]
    [InlineData(Shell.Zsh)]
    public void Setup_PosixShellsExportQuotedValuesAndPutToolsFirst(Shell shell)
    {
        var setup = ShellScripts.Setup(shell, new[] { "/a", "/b" }, Envs, Env(("PATH", "/usr/bin")));

        Assert.Equal(new[]
        {
            "export DT_BASEPATH='/usr/bin'",
            "export JAVA_HOME='/tools/java/25'",
            @"export GREETING='it'\''s $HOME `x`'",
            "export DT_ENVS='JAVA_HOME GREETING'",
            "export DT_PATH='/a:/b'",
            "export PATH='/a:/b:/usr/bin'",
            "export DT_INITIALIZED='1'"
        }, setup.Split('\n'));
    }

    [Fact]
    public void Setup_CmdSetsQuotedAssignments()
    {
        var setup = ShellScripts.Setup(Shell.Cmd, new[] { @"D:\a", @"D:\b" }, new KeyValuePair<string, string>[] { new("ProgramFiles(x86)", "x & y") }, Env(("PATH", @"C:\Windows")));

        Assert.Equal(new[]
        {
            @"set ""DT_BASEPATH=C:\Windows""",
            @"set ""ProgramFiles(x86)=x & y""",
            @"set ""DT_ENVS=ProgramFiles(x86)""",
            @"set ""DT_PATH=D:\a;D:\b""",
            @"set ""PATH=D:\a;D:\b;C:\Windows""",
            @"set ""DT_INITIALIZED=1"""
        }, setup.Split("\r\n"));
    }

    [Theory]
    [InlineData(Shell.Cmd)]
    [InlineData(Shell.Bash)]
    public void Setup_RebuildsPathFromBasePath(Shell shell)
    {
        var setup = ShellScripts.Setup(shell, new[] { "new" }, Array.Empty<KeyValuePair<string, string>>(), Env(("PATH", "old;base"), ("DT_BASEPATH", "base")));

        Assert.Contains(shell == Shell.Cmd ? @"set ""PATH=new;base""" : "export PATH='new:base'", setup);
        Assert.DoesNotContain("old", setup);
    }

    [Fact]
    public void Setup_EmptyBasePathFallsBackToPath()
    {
        var setup = ShellScripts.Setup(Shell.Bash, new[] { "/a" }, Array.Empty<KeyValuePair<string, string>>(), Env(("PATH", "/usr/bin"), ("DT_BASEPATH", "")));

        Assert.Contains("export PATH='/a:/usr/bin'", setup);
    }

    [Theory]
    [InlineData(Shell.Cmd, @"set ""OLD=""")]
    [InlineData(Shell.Bash, "unset OLD")]
    public void Setup_UnsetsVariablesNoLongerSet(Shell shell, string unset)
    {
        var setup = ShellScripts.Setup(shell, Array.Empty<string>(), Envs, Env(("PATH", "p"), ("DT_ENVS", "OLD JAVA_HOME")));

        Assert.Contains(unset, setup);
        Assert.DoesNotContain("JAVA_HOME=\"", setup);
        Assert.DoesNotContain("unset JAVA_HOME", setup);
    }

    [Theory]
    [InlineData(Shell.Bash, "ProgramFiles(x86)", "x")]
    [InlineData(Shell.Cmd, "QUOTED", "a\"b")]
    [InlineData(Shell.Cmd, "MULTILINE", "a\nb")]
    public void Setup_SkipsVariablesTheShellCannotSet(Shell shell, string key, string value)
    {
        var output = TestTools.CaptureError(() =>
        {
            var setup = ShellScripts.Setup(shell, Array.Empty<string>(), new KeyValuePair<string, string>[] { new(key, value), new("OK", "y") }, Env());

            Assert.DoesNotContain(key, setup);
            Assert.Contains("OK", setup);
        });

        Assert.Contains(key, output);
    }

    [Theory]
    [InlineData(Shell.Bash, "bash")]
    [InlineData(Shell.Zsh, "zsh")]
    public void Init_PosixShellsDefineDtAndReload(Shell shell, string name)
    {
        var script = ShellScripts.Init(shell, "/opt/dev tools/devtools");

        Assert.Contains($"setup=\"$('/opt/dev tools/devtools' setup --shell {name})\"", script);
        Assert.Contains("dt() {", script);
        Assert.DoesNotContain("\r", script);
    }

    [Fact]
    public void Init_CmdMarksInitializedBeforeRunningSetup()
    {
        var command = ShellScripts.Init(Shell.Cmd, @"C:\Program Files\devtools\devtools.exe");

        Assert.Equal("""if not defined DT_INITIALIZED (set "DT_INITIALIZED=1" & for /f "delims=" %i in ('""C:\Program Files\devtools\devtools.exe" setup --shell cmd"') do @%i)""", command);
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
    public void RemoveFromRc_RemovesOnlyDevtoolsLines(Shell shell, string content, string expected)
    {
        Assert.Equal(expected, ShellScripts.RemoveFromRc(content, shell));
    }

    [Fact]
    public void RemoveFromRc_UndoesInstall()
    {
        var original = "alias ll='ls -l'\n";
        var installed = original + $"\n{ShellScripts.RcMarker}\n{ShellScripts.RcLine(Shell.Bash, "/opt/devtools/devtools")}\n";

        Assert.Equal(original, ShellScripts.RemoveFromRc(installed, Shell.Bash));
    }

    [Fact]
    public void RcLine_EvaluatesInit()
    {
        Assert.Equal("""eval "$('/opt/devtools' init zsh)" """.TrimEnd(), ShellScripts.RcLine(Shell.Zsh, "/opt/devtools"));
    }
}
