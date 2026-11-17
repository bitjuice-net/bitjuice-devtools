using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DevTools.Catalog;
using DevTools.Infrastructure;
using DevTools.Management;
using Microsoft.Extensions.Options;
using Xunit;

namespace DevTools.Tests;

public class MetaVersionTests
{
    private readonly MemoryStorage storage = TestTools.Storage(("node", new[] { "8.9.3", "24.11.1", "12.18.1" }));
    private readonly ToolDefinitionProvider definitions;
    private readonly ToolSettingsProvider settings;
    private readonly ToolManager manager;

    public MetaVersionTests()
    {
        definitions = new ToolDefinitionProvider(storage);
        settings = new ToolSettingsProvider(storage, definitions);
        manager = new ToolManager(Options.Create(new Settings()), definitions, settings, null);
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
    public void GetVersion_ExactVersionWinsOverMetaVersion()
    {
        var special = new ToolDefinitionProvider(TestTools.Storage(("x", new[] { "1.0.0", "latest", "2.0.0" })));

        Assert.Equal("latest", special.GetVersion("x", "latest").Manifest.Version);
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
        Assert.Contains(@"C:\Tools\node\24.11.1", TestTools.CaptureConsole(() => manager.GetEnv(Shell.Bash)));

        storage.Load<Dictionary<string, List<ToolDefinition>>>(TestTools.CacheFile)["node"].Add(TestTools.Definition("node", "25.0.0"));

        Assert.Contains(@"C:\Tools\node\25.0.0", TestTools.CaptureConsole(() => manager.GetEnv(Shell.Bash)));
    }

    [Fact]
    public void List_ShowsResolvedVersionAndSummarizesManyVersions()
    {
        TestTools.CaptureConsole(() => manager.Select("node", "oldest"));
        storage.Load<Dictionary<string, List<ToolDefinition>>>(TestTools.CacheFile)["node"].Add(TestTools.Definition("node", "25.0.0"));

        var row = TestTools.CaptureConsole(() => manager.List(false)).Split('\n').Single(i => i.StartsWith("node "));

        Assert.Equal(new[] { "node", "oldest (8.9.3)", "8.9.3 - 25.0.0 (4 versions)" }, row.Split("  ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
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
        Assert.Contains(@"path       C:\Tools\node\24.11.1", output);
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
