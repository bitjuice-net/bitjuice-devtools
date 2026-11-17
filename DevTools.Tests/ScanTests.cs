using System;
using System.IO;
using System.Linq;
using DevTools.Catalog;
using DevTools.Infrastructure;
using DevTools.Management;
using Microsoft.Extensions.Options;
using Xunit;

namespace DevTools.Tests;

public sealed class ScanTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("devtools-tests-").FullName;
    private readonly MemoryStorage storage = new();
    private readonly ToolDefinitionProvider definitions;

    public ScanTests()
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
        Dirs("java/11.0.1", "java/25.0.5", "java/docs", "java/.install-1.0.0-abc");

        definitions.Scan(root);

        Assert.Equal(new[] { "11.0.1", "25.0.5" }, Versions("java"));
        Assert.Equal(Path.Combine(root, "java", "25.0.5"), definitions.GetVersion("java", "latest").Path);
    }

    [Fact]
    public void Versioned_MergesDirectoriesWithTheSameName()
    {
        Manifest("mingw", """{ "name": "mingw", "versioned": true, "paths": ["%APPDIR%\\bin"] }""");
        Manifest("mingw-legacy", """{ "name": "mingw", "versioned": true, "paths": ["%APPDIR%\\bin", "%APPDIR%\\msys"] }""");
        Dirs("mingw/15.2.0", "mingw-legacy/1.0.0");

        definitions.Scan(root);

        Assert.Equal(new[] { "1.0.0", "15.2.0" }, Versions("mingw"));
        Assert.Equal(2, definitions.GetVersion("mingw", "1.0.0").Manifest.Paths.Count);
        Assert.Single(definitions.GetVersion("mingw", "15.2.0").Manifest.Paths);
    }

    [Fact]
    public void Versioned_ThrowsOnSameVersionInTwoDirectoriesAndKeepsCache()
    {
        Manifest("mingw", """{ "name": "mingw", "versioned": true }""");
        Manifest("mingw-legacy", """{ "name": "mingw", "versioned": true }""");
        Dirs("mingw/1.0.0", "mingw-legacy/1.0.0");

        var error = Assert.Throws<InvalidOperationException>(() => definitions.Scan(root));

        Assert.Contains(Path.Combine(root, "mingw", "1.0.0"), error.Message);
        Assert.Contains(Path.Combine(root, "mingw-legacy", "1.0.0"), error.Message);
        Assert.Empty(definitions.GetNames());
    }

    [Fact]
    public void Versioned_ThrowsOnTwoPartAndThreePartNamesOfOneVersion()
    {
        Manifest("java", """{ "name": "java", "versioned": true }""");
        Dirs("java/1.2", "java/1.2.0");

        var error = Assert.Throws<InvalidOperationException>(() => definitions.Scan(root));

        Assert.Contains(Path.Combine(root, "java", "1.2"), error.Message);
    }

    [Fact]
    public void Versioned_FindsVersionByEitherName()
    {
        Manifest("java", """{ "name": "java", "versioned": true }""");
        Dirs("java/1.2.0", "java/103.0");

        definitions.Scan(root);

        Assert.Equal("1.2.0", definitions.GetVersion("java", "1.2").Manifest.Version);
        Assert.Equal("103.0", definitions.GetVersion("java", "103.0.0").Manifest.Version);
        Assert.Equal(new[] { "1.2.0", "103.0" }, Versions("java"));
    }

    [Fact]
    public void Versioned_IgnoresLegacyManifestInVersionDirectory()
    {
        Manifest("java", """{ "name": "java", "versioned": true }""");
        Manifest("java/11.0.1", """{ "name": "java", "version": "%DIRNAME%" }""");

        definitions.Scan(root);

        Assert.Equal(new[] { "11.0.1" }, Versions("java"));
    }

    [Fact]
    public void Unversioned_DoesNotTreatSubdirectoriesAsVersions()
    {
        Manifest("android-sdk", """{ "name": "android-sdk" }""");
        Dirs("android-sdk/34.0.0", "android-sdk/platform-tools");

        definitions.Scan(root);

        Assert.Equal(new[] { "default" }, Versions("android-sdk"));
    }

    [Fact]
    public void Unversioned_ThrowsOnTwoDirectoriesWithTheSameName()
    {
        Manifest("a", """{ "name": "x" }""");
        Manifest("b", """{ "name": "x" }""");

        var error = Assert.Throws<InvalidOperationException>(() => definitions.Scan(root));

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
        definitions.Scan(root);
        Manifest("x", content);

        var error = Assert.Throws<InvalidOperationException>(() => definitions.Scan(root));

        Assert.Contains(Path.Combine(root, "x"), error.Message);
        Assert.Contains("remove it", error.Message);
        Assert.Equal(new[] { "good" }, definitions.GetNames());
    }

    private ToolManager ScanUnversioned(out ToolSettingsProvider settings)
    {
        Manifest("bin", """{ "name": "bin", "paths": ["%APPDIR%\\x"] }""");
        settings = new ToolSettingsProvider(storage, definitions);
        definitions.Scan(root);
        return new ToolManager(Options.Create(new Settings { RootPath = root }), definitions, settings, null);
    }

    [Fact]
    public void Unversioned_IsOnThePathAfterScan()
    {
        var manager = ScanUnversioned(out _);

        Assert.Contains(Path.Combine(root, "bin", "x"), TestTools.CaptureConsole(() => manager.GetEnv(Shell.Bash)));
    }

    [Fact]
    public void Env_PutsApplicationPathsInOrder()
    {
        Manifest("a", """{ "name": "a", "paths": ["%APPDIR%"] }""");
        var manager = ScanUnversioned(out _);

        var setup = TestTools.CaptureConsole(() => manager.GetEnv(Shell.Bash));

        Assert.Contains($"export DT_PATH='{Path.Combine(root, "a")}:{Path.Combine(root, "bin", "x")}:", setup);
    }

    [Fact]
    public void Env_BashExportsPathAndEnvs()
    {
        Manifest("java", """{ "name": "java", "paths": ["%APPDIR%\\bin"], "envs": { "JAVA_HOME": "%APPDIR%" } }""");
        var manager = ScanUnversioned(out _);

        var setup = TestTools.CaptureConsole(() => manager.GetEnv(Shell.Bash));

        Assert.Contains($"export JAVA_HOME='{Path.Combine(root, "java")}'", setup);
        Assert.Contains("export DT_ENVS='JAVA_HOME'", setup);
        Assert.Contains("export DT_PATH='", setup);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("default")]
    public void Unversioned_KeepsWorkingWithStoredOrDefaultSelection(string selected)
    {
        var manager = ScanUnversioned(out var settings);
        if (selected != null)
            settings.UpdateSettings("bin", s => s.Version = selected);

        var row = TestTools.CaptureConsole(() => manager.List(false)).Split('\n').Single(i => i.StartsWith("bin "));

        Assert.Equal(new[] { "bin", "-", "-" }, row.Split("  ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        Assert.Contains(Path.Combine(root, "bin", "x"), TestTools.CaptureConsole(() => manager.GetEnv(Shell.Bash)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("latest")]
    public void Unversioned_SelectExplainsAndChangesNothing(string version)
    {
        var manager = ScanUnversioned(out var settings);
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

        var error = Assert.Throws<InvalidOperationException>(() => definitions.Scan(root));

        Assert.Contains(Path.Combine(root, "x"), error.Message);
    }

    [Fact]
    public void Prune_KeepsMetaVersionsAndRemovesStaleEntries()
    {
        Manifest("java", """{ "name": "java", "versioned": true }""");
        Dirs("java/11.0.1");
        var settings = new ToolSettingsProvider(storage, definitions);
        var manager = new ToolManager(Options.Create(new Settings { RootPath = root }), definitions, settings, null);
        definitions.Scan(root);
        settings.UpdateSettings("java", s => s.Version = "latest");
        settings.UpdateSettings("ghost", s => s.Version = "1.0.0");

        TestTools.CaptureConsole(manager.Prune);

        Assert.Equal(new[] { "java" }, settings.GetApplications());
        Assert.Equal("latest", settings.GetSettings("java").Version);
    }
}