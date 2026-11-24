using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DevTools.Catalog;
using DevTools.Catalog.Models;
using DevTools.Infrastructure;
using DevTools.Management;
using Microsoft.Extensions.Options;
using Xunit;

namespace DevTools.Tests;

public sealed class MetaVersionTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("devtools-tests-").FullName;
    private readonly MemoryStorage storage = new();
    private readonly ToolDefinitionProvider definitions;
    private readonly ToolSettingsProvider settings;
    private readonly ToolManager manager;

    public MetaVersionTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "node"));
        File.WriteAllText(Path.Combine(root, "node", ".app"), """{ "name": "node", "paths": ["%APPDIR%"] }""");
        foreach (var version in new[] { "8.9.3", "24.11.1", "12.18.1" })
            AddVersion(version);

        definitions = new ToolDefinitionProvider(storage);
        settings = new ToolSettingsProvider(storage, definitions);
        manager = Manager();
        definitions.Scan(root);
    }

    public void Dispose()
    {
        Directory.Delete(root, true);
    }

    private void AddVersion(string version)
    {
        Directory.CreateDirectory(Path.Combine(root, "node", version));
    }

    // Every command scans once, like a new devtools process.
    private ToolManager Manager()
    {
        return new ToolManager(Options.Create(new Settings { RootPath = root }), definitions, settings, null);
    }

    private string Node(string version)
    {
        return Path.Combine(root, "node", version);
    }

    [Theory]
    [InlineData("latest", "24.11.1")]
    [InlineData("oldest", "8.9.3")]
    [InlineData("12.18.1", "12.18.1")]
    public void GetVersion_ResolvesVersions(string requested, string expected)
    {
        Assert.Equal(expected, definitions.GetVersion("node", requested).Manifest.Version);
    }

    [Fact]
    public void GetVersion_ReturnsNullForUnknownVersionOrApplication()
    {
        Assert.Null(definitions.GetVersion("node", "1.0.0"));
        Assert.Null(definitions.GetVersion("ghost", "latest"));
    }

    [Fact]
    public void Select_StoresMetaVersionAndNotTheResolvedVersion()
    {
        TestTools.CaptureConsole(() => manager.Select("node", "latest"));

        Assert.Equal("latest", settings.GetSettings("node").Version);
    }

    [Theory]
    [InlineData("0", "latest")]
    [InlineData("1", "24.11.1")]
    [InlineData("3", "8.9.3")]
    [InlineData("4", "oldest")]
    public void Select_WithoutVersionListsLatestFirstAndOldestLast(string input, string expected)
    {
        var input0 = Console.In;
        Console.SetIn(new StringReader(input + Environment.NewLine));
        try
        {
            var output = TestTools.CaptureConsole(() => manager.Select("node", null));

            Assert.Contains("[0] latest\n[1] 24.11.1\n[2] 12.18.1\n[3] 8.9.3\n[4] oldest", output.ReplaceLineEndings("\n"));
            Assert.Equal(expected, settings.GetSettings("node").Version);
        }
        finally
        {
            Console.SetIn(input0);
        }
    }

    [Fact]
    public void GetSettings_DefaultsToLatest()
    {
        Assert.Equal("latest", settings.GetSettings("node").Version);
    }

    [Fact]
    public void Env_FollowsNewVersionWhenCacheChanges()
    {
        TestTools.CaptureConsole(() => manager.Select("node", "latest"));
        Assert.Contains(Node("24.11.1"), TestTools.CaptureConsole(() => manager.GetEnv(Shell.Bash)));

        AddVersion("25.0.0");

        Assert.Contains(Node("25.0.0"), TestTools.CaptureConsole(() => Manager().GetEnv(Shell.Bash)));
    }

    [Fact]
    public void List_ShowsResolvedVersionAndSummarizesManyVersions()
    {
        TestTools.CaptureConsole(() => manager.Select("node", "oldest"));
        AddVersion("25.0.0");

        var row = TestTools.CaptureConsole(() => Manager().List(false)).Split('\n').Single(i => i.StartsWith("node "));

        Assert.Equal(new[] { "node", "oldest (8.9.3)", "8.9.3 - 25.0.0 (4 versions)", "local" }, row.Split("  ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    [Fact]
    public void List_MarksDisabledApplications()
    {
        TestTools.CaptureConsole(() => manager.SetDisabled("node", true));

        var row = TestTools.CaptureConsole(() => manager.List(false)).Split('\n').Single(i => i.StartsWith("node "));

        Assert.EndsWith("disabled", row.TrimEnd());
    }

    [Fact]
    public void Show_ListsVersionsNewestFirstAndWhatTheSelectedOneSets()
    {
        var output = TestTools.CaptureConsole(() => manager.Show("node"));

        Assert.Contains("versions   24.11.1, 12.18.1, 8.9.3", output);
        Assert.Contains("selected   latest (24.11.1)", output);
        Assert.Contains($"path       {Node("24.11.1")}", output);
    }

    [Fact]
    public void List_MarksMetaVersionWithoutAnyInstalledVersionAsOrphaned()
    {
        settings.UpdateSettings("ghost", s => s.Version = "latest");

        var row = TestTools.CaptureConsole(() => manager.List(true)).Split('\n').Single(i => i.StartsWith("ghost "));

        Assert.EndsWith("orphaned", row.TrimEnd());
    }

    [Fact]
    public void Select_UnknownApplicationOrVersionIsAnError()
    {
        Assert.Contains("ghost not found", Assert.Throws<InvalidOperationException>(() => manager.Select("ghost", "latest")).Message);
        Assert.Contains("installed: 8.9.3, 12.18.1, 24.11.1", Assert.Throws<InvalidOperationException>(() => manager.Select("node", "99")).Message);
    }

    [Fact]
    public void Select_WithoutInputKeepsTheVersion()
    {
        TestTools.CaptureConsole(() => manager.Select("node", "oldest"));
        var input = Console.In;
        Console.SetIn(new StringReader(Environment.NewLine));
        try
        {
            Assert.Contains("node still uses oldest (8.9.3)", TestTools.CaptureConsole(() => manager.Select("node", null)));
            Assert.Equal("oldest", settings.GetSettings("node").Version);
        }
        finally
        {
            Console.SetIn(input);
        }
    }

    [Fact]
    public void SetDisabled_UnknownApplicationIsAnErrorAndNotStored()
    {
        Assert.Throws<InvalidOperationException>(() => manager.SetDisabled("ghost", true));

        Assert.DoesNotContain("ghost", settings.GetApplications());
    }

    [Fact]
    public void SetDisabled_SaysWhatChanged()
    {
        Assert.Contains("node is disabled", TestTools.CaptureConsole(() => manager.SetDisabled("node", true)));
        Assert.True(settings.GetSettings("node").IsDisabled);
    }
}
