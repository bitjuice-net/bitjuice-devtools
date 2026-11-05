using System;
using System.IO;
using System.Linq;
using DevTools.Catalog;
using DevTools.Infrastructure;
using DevTools.Management;
using Microsoft.Extensions.Options;
using Xunit;

namespace DevTools.Tests;

public sealed class DiscoverTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("devtools-tests-").FullName;
    private readonly MemoryStorage storage = new();
    private readonly ToolDefinitionProvider definitions;

    public DiscoverTests()
    {
        definitions = new ToolDefinitionProvider(storage);
    }

    public void Dispose()
    {
        Directory.Delete(root, true);
    }

    private void Manifest(string directory, string content)
    {
        var path = Path.Combine(root, directory);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, ".app"), content);
    }

    private void Dirs(params string[] directories)
    {
        foreach (var directory in directories)
            Directory.CreateDirectory(Path.Combine(root, directory));
    }

    private string[] Versions(string application)
    {
        return definitions.GetVersions(application).Select(i => i.Manifest.Version).ToArray();
    }

    [Fact]
    public void Versioned_CreatesVersionFromEachVersionDirectoryOnly()
    {
        Manifest("java", """{ "name": "java", "versioned": true, "paths": ["%APPDIR%\\bin"] }""");
        Dirs(@"java\11.0.1", @"java\25.0.5", @"java\docs", @"java\.install-1.0.0-abc");

        definitions.Discover(root);

        Assert.Equal(new[] { "11.0.1", "25.0.5" }, Versions("java"));
        Assert.Equal(Path.Combine(root, "java", "25.0.5"), definitions.GetVersion("java", "latest").Path);
    }

    [Fact]
    public void Versioned_MergesDirectoriesWithTheSameName()
    {
        Manifest("mingw", """{ "name": "mingw", "versioned": true, "paths": ["%APPDIR%\\bin"] }""");
        Manifest("mingw-legacy", """{ "name": "mingw", "versioned": true, "paths": ["%APPDIR%\\bin", "%APPDIR%\\msys"] }""");
        Dirs(@"mingw\15.2.0", @"mingw-legacy\1.0.0");

        definitions.Discover(root);

        Assert.Equal(new[] { "1.0.0", "15.2.0" }, Versions("mingw"));
        Assert.Equal(2, definitions.GetVersion("mingw", "1.0.0").Manifest.Paths.Count);
        Assert.Single(definitions.GetVersion("mingw", "15.2.0").Manifest.Paths);
    }

    [Fact]
    public void Versioned_ThrowsOnSameVersionInTwoDirectoriesAndKeepsCache()
    {
        Manifest("mingw", """{ "name": "mingw", "versioned": true }""");
        Manifest("mingw-legacy", """{ "name": "mingw", "versioned": true }""");
        Dirs(@"mingw\1.0.0", @"mingw-legacy\1.0.0");

        var error = Assert.Throws<InvalidOperationException>(() => definitions.Discover(root));

        Assert.Contains(Path.Combine(root, "mingw", "1.0.0"), error.Message);
        Assert.Contains(Path.Combine(root, "mingw-legacy", "1.0.0"), error.Message);
        Assert.Empty(definitions.GetNames());
    }

    [Fact]
    public void Versioned_IgnoresLegacyManifestInVersionDirectory()
    {
        Manifest("java", """{ "name": "java", "versioned": true }""");
        Manifest(@"java\11.0.1", """{ "name": "java", "version": "%DIRNAME%" }""");

        definitions.Discover(root);

        Assert.Equal(new[] { "11.0.1" }, Versions("java"));
    }

    [Fact]
    public void Unversioned_DoesNotTreatSubdirectoriesAsVersions()
    {
        Manifest("android-sdk", """{ "name": "android-sdk" }""");
        Dirs(@"android-sdk\34.0.0", @"android-sdk\platform-tools");

        definitions.Discover(root);

        Assert.Equal(new[] { "default" }, Versions("android-sdk"));
    }

    [Fact]
    public void Unversioned_ThrowsOnTwoDirectoriesWithTheSameName()
    {
        Manifest("a", """{ "name": "x" }""");
        Manifest("b", """{ "name": "x" }""");

        var error = Assert.Throws<InvalidOperationException>(() => definitions.Discover(root));

        Assert.Contains(Path.Combine(root, "a"), error.Message);
        Assert.Contains(Path.Combine(root, "b"), error.Message);
    }

    [Theory]
    [InlineData("""{ "name": "x", "version": "default" }""")]
    [InlineData("""{ "name": "x", "version": "%DIRNAME%" }""")]
    [InlineData("""{ "name": "x", "version": "1.0.0", "versioned": true }""")]
    public void VersionInManifest_ThrowsAndKeepsCache(string content)
    {
        Manifest("good", """{ "name": "good" }""");
        definitions.Discover(root);
        Manifest("x", content);

        var error = Assert.Throws<InvalidOperationException>(() => definitions.Discover(root));

        Assert.Contains(Path.Combine(root, "x"), error.Message);
        Assert.Contains("remove it", error.Message);
        Assert.Equal(new[] { "good" }, definitions.GetNames());
    }

    private ToolManager DiscoverUnversioned(out ToolSettingsProvider settings)
    {
        Manifest("bin", """{ "name": "bin", "paths": ["%APPDIR%\\x"] }""");
        settings = new ToolSettingsProvider(storage, definitions);
        definitions.Discover(root);
        return new ToolManager(Options.Create(new Settings { RootPath = root }), definitions, settings, null);
    }

    [Fact]
    public void Unversioned_IsOnThePathAfterDiscover()
    {
        var manager = DiscoverUnversioned(out _);

        Assert.Contains(Path.Combine(root, "bin", "x"), TestTools.CaptureConsole(manager.GetPath));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("default")]
    public void Unversioned_KeepsWorkingWithStoredOrDefaultSelection(string selected)
    {
        var manager = DiscoverUnversioned(out var settings);
        if (selected != null)
            settings.UpdateSettings("bin", s => s.Version = selected);

        var row = TestTools.CaptureConsole(() => manager.List(false)).Split('\n').Single(i => i.StartsWith("| bin"));

        Assert.Equal(new[] { "bin", "default", "default", "Enabled" }, row.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        Assert.Contains(Path.Combine(root, "bin", "x"), TestTools.CaptureConsole(manager.GetPath));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("latest")]
    public void Unversioned_SelectExplainsAndChangesNothing(string version)
    {
        var manager = DiscoverUnversioned(out var settings);
        var before = settings.GetSettings("bin").Version;

        var output = TestTools.CaptureConsole(() => manager.Select("bin", version));

        Assert.Contains("not versioned", output);
        Assert.Equal(before, settings.GetSettings("bin").Version);
    }

    [Theory]
    [InlineData("""{ "version": "1.0.0" }""")]
    [InlineData("""{ "versioned": true }""")]
    public void InvalidManifest_ThrowsWithItsDirectory(string content)
    {
        Manifest("x", content);

        var error = Assert.Throws<InvalidOperationException>(() => definitions.Discover(root));

        Assert.Contains(Path.Combine(root, "x"), error.Message);
    }

    [Fact]
    public void Prune_KeepsMetaVersionsAndRemovesStaleEntries()
    {
        Manifest("java", """{ "name": "java", "versioned": true }""");
        Dirs(@"java\11.0.1");
        var settings = new ToolSettingsProvider(storage, definitions);
        var manager = new ToolManager(Options.Create(new Settings { RootPath = root }), definitions, settings, null);
        definitions.Discover(root);
        settings.UpdateSettings("java", s => s.Version = "latest");
        settings.UpdateSettings("ghost", s => s.Version = "1.0.0");

        TestTools.CaptureConsole(manager.Prune);

        Assert.Equal(new[] { "java" }, settings.GetApplications());
        Assert.Equal("latest", settings.GetSettings("java").Version);
    }
}