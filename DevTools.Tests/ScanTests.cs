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

    // Times a minute old, so a scan right after creating the files can trust them.
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
        Manifest("java", """{ "name": "java", "paths": ["%APPDIR%\\bin"] }""");
        Dirs("java/11.0.1", "java/25.0.5", "java/docs", "java/.install-1.0.0-abc");

        definitions.Scan(root);

        Assert.Equal(new[] { "11.0.1", "25.0.5" }, Versions("java"));
        Assert.Equal(Path.Combine(root, "java", "25.0.5"), definitions.GetVersion("java", "latest").Path);
    }

    [Fact]
    public void Versioned_MergesDirectoriesWithTheSameName()
    {
        Manifest("mingw", """{ "name": "mingw", "paths": ["%APPDIR%\\bin"] }""");
        Manifest("mingw-legacy", """{ "name": "mingw", "paths": ["%APPDIR%\\bin", "%APPDIR%\\msys"] }""");
        Dirs("mingw/15.2.0", "mingw-legacy/1.0.0");

        definitions.Scan(root);

        Assert.Equal(new[] { "1.0.0", "15.2.0" }, Versions("mingw"));
        Assert.Equal(2, definitions.GetVersion("mingw", "1.0.0").Manifest.Paths.Count);
        Assert.Single(definitions.GetVersion("mingw", "15.2.0").Manifest.Paths);
    }

    [Fact]
    public void Versioned_ThrowsOnSameVersionInTwoDirectoriesAndKeepsCache()
    {
        Manifest("mingw", """{ "name": "mingw" }""");
        Manifest("mingw-legacy", """{ "name": "mingw" }""");
        Dirs("mingw/1.0.0", "mingw-legacy/1.0.0");

        var error = Assert.Throws<InvalidOperationException>(() => definitions.Scan(root));

        Assert.Contains(Path.Combine(root, "mingw", "1.0.0"), error.Message);
        Assert.Contains(Path.Combine(root, "mingw-legacy", "1.0.0"), error.Message);
        Assert.Empty(definitions.GetNames());
    }

    [Fact]
    public void Versioned_ThrowsOnTwoPartAndThreePartNamesOfOneVersion()
    {
        Manifest("java", """{ "name": "java" }""");
        Dirs("java/1.2", "java/1.2.0");

        var error = Assert.Throws<InvalidOperationException>(() => definitions.Scan(root));

        Assert.Contains(Path.Combine(root, "java", "1.2"), error.Message);
    }

    [Fact]
    public void Versioned_FindsVersionByEitherName()
    {
        Manifest("java", """{ "name": "java" }""");
        Dirs("java/1.2.0", "java/103.0");

        definitions.Scan(root);

        Assert.Equal("1.2.0", definitions.GetVersion("java", "1.2").Manifest.Version);
        Assert.Equal("103.0", definitions.GetVersion("java", "103.0.0").Manifest.Version);
        Assert.Equal(new[] { "1.2.0", "103.0" }, Versions("java"));
    }

    [Fact]
    public void Versioned_IgnoresLegacyManifestInVersionDirectory()
    {
        Manifest("java", """{ "name": "java" }""");
        Manifest("java/11.0.1", """{ "name": "java", "version": "%DIRNAME%" }""");

        definitions.Scan(root);

        Assert.Equal(new[] { "11.0.1" }, Versions("java"));
    }

    [Fact]
    public void Flat_DoesNotTreatSubdirectoriesAsVersions()
    {
        Manifest("android-sdk", """{ "name": "android-sdk", "flat": true }""");
        Dirs("android-sdk/34.0.0", "android-sdk/platform-tools");

        definitions.Scan(root);

        Assert.Equal(new[] { "default" }, Versions("android-sdk"));
    }

    [Fact]
    public void Flat_ThrowsOnTwoDirectoriesWithTheSameName()
    {
        Manifest("a", """{ "name": "x", "flat": true }""");
        Manifest("b", """{ "name": "x", "flat": true }""");

        var error = Assert.Throws<InvalidOperationException>(() => definitions.Scan(root));

        Assert.Contains(Path.Combine(root, "a"), error.Message);
        Assert.Contains(Path.Combine(root, "b"), error.Message);
    }

    [Theory]
    [InlineData("""{ "name": "x", "version": "default" }""")]
    [InlineData("""{ "name": "x", "version": "%DIRNAME%" }""")]
    [InlineData("""{ "name": "x", "version": "1.0.0" }""")]
    public void VersionInManifest_ThrowsAndKeepsCache(string content)
    {
        Manifest("good", """{ "name": "good", "flat": true }""");
        definitions.Scan(root);
        Manifest("x", content);

        var error = Assert.Throws<InvalidOperationException>(() => definitions.Scan(root));

        Assert.Contains(Path.Combine(root, "x"), error.Message);
        Assert.Contains("remove it", error.Message);
        Assert.Equal(new[] { "good" }, definitions.GetNames());
    }

    private ToolManager ScanFlat(out ToolSettingsProvider settings)
    {
        Manifest("bin", """{ "name": "bin", "flat": true, "paths": ["%APPDIR%\\x"] }""");
        settings = new ToolSettingsProvider(storage, definitions);
        definitions.Scan(root);
        return new ToolManager(Options.Create(new Settings { RootPath = root }), definitions, settings, null);
    }

    [Fact]
    public void Flat_IsOnThePathAfterScan()
    {
        var manager = ScanFlat(out _);

        Assert.Contains(Path.Combine(root, "bin", "x"), TestTools.CaptureConsole(() => manager.GetEnv(Shell.Bash)));
    }

    [Fact]
    public void Env_PutsApplicationPathsInOrder()
    {
        Manifest("a", """{ "name": "a", "flat": true, "paths": ["%APPDIR%"] }""");
        var manager = ScanFlat(out _);

        var setup = TestTools.CaptureConsole(() => manager.GetEnv(Shell.Bash));

        Assert.Contains($"export DT_PATH='{Path.Combine(root, "a")}:{Path.Combine(root, "bin", "x")}:", setup);
    }

    [Fact]
    public void Env_BashExportsPathAndEnvs()
    {
        Manifest("java", """{ "name": "java", "flat": true, "paths": ["%APPDIR%\\bin"], "envs": { "JAVA_HOME": "%APPDIR%" } }""");
        var manager = ScanFlat(out _);

        var setup = TestTools.CaptureConsole(() => manager.GetEnv(Shell.Bash));

        Assert.Contains($"export JAVA_HOME='{Path.Combine(root, "java")}'", setup);
        Assert.Contains("export DT_ENVS='JAVA_HOME'", setup);
        Assert.Contains("export DT_PATH='", setup);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("default")]
    public void Flat_KeepsWorkingWithStoredOrDefaultSelection(string selected)
    {
        var manager = ScanFlat(out var settings);
        if (selected != null)
            settings.UpdateSettings("bin", s => s.Version = selected);

        var row = TestTools.CaptureConsole(() => manager.List(false)).Split('\n').Single(i => i.StartsWith("bin "));

        Assert.Equal(new[] { "bin", "-", "-", "local" }, row.Split("  ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        Assert.Contains(Path.Combine(root, "bin", "x"), TestTools.CaptureConsole(() => manager.GetEnv(Shell.Bash)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("latest")]
    public void Flat_SelectExplainsAndChangesNothing(string version)
    {
        var manager = ScanFlat(out var settings);
        var before = settings.GetSettings("bin").Version;

        var output = TestTools.CaptureConsole(() => manager.Select("bin", version));

        Assert.Contains("is flat", output);
        Assert.Equal(before, settings.GetSettings("bin").Version);
    }

    [Theory]
    [InlineData("""{ "version": "1.0.0" }""")]
    [InlineData("""{ "flat": true }""")]
    public void InvalidManifest_ThrowsWithItsDirectory(string content)
    {
        Manifest("x", content);

        var error = Assert.Throws<InvalidOperationException>(() => definitions.Scan(root));

        Assert.Contains(Path.Combine(root, "x"), error.Message);
    }

    [Fact]
    public void Prune_KeepsMetaVersionsAndRemovesStaleEntries()
    {
        Manifest("java", """{ "name": "java" }""");
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

    private ToolManager Manager(ToolSettingsProvider tools = null)
    {
        return new ToolManager(Options.Create(new Settings { RootPath = root }), definitions, tools ?? new ToolSettingsProvider(storage, definitions), null);
    }

    [Fact]
    public void List_ShowsVersionsAddedByHandOnlyAfterAScan()
    {
        Manifest("java", """{ "name": "java", "paths": ["%APPDIR%"] }""");
        Dirs("java/1.0.0");
        definitions.Scan(root);
        Dirs("java/2.0.0");

        var before = TestTools.CaptureConsole(() => Manager().List(false)).Split('\n').Single(i => i.StartsWith("java "));
        Manager().Scan();
        var after = TestTools.CaptureConsole(() => Manager().List(false)).Split('\n').Single(i => i.StartsWith("java "));

        Assert.DoesNotContain("2.0.0", before);
        Assert.Contains("1.0.0, 2.0.0", after);
    }

    [Fact]
    public void Select_FindsAVersionAddedByHandWithOneScan()
    {
        Manifest("java", """{ "name": "java", "paths": ["%APPDIR%"] }""");
        Dirs("java/1.0.0");
        var tools = new ToolSettingsProvider(storage, definitions);
        definitions.Scan(root);
        Dirs("java/2.0.0");

        TestTools.CaptureConsole(() => Manager(tools).Select("java", "2.0.0"));

        Assert.Equal("2.0.0", tools.GetSettings("java").Version);
    }

    [Fact]
    public void Show_FindsAnApplicationAddedByHandWithOneScan()
    {
        Manifest("java", """{ "name": "java", "paths": ["%APPDIR%"] }""");
        definitions.Scan(root);
        Manifest("go", """{ "name": "go", "paths": ["%APPDIR%"] }""");
        Dirs("go/1.0.0");

        Assert.Contains(Path.Combine(root, "go"), TestTools.CaptureConsole(() => Manager().Show("go")));
        Assert.Contains("not found", Assert.Throws<InvalidOperationException>(() => Manager().Show("ghost")).Message);
    }

    [Fact]
    public void Env_ScansWhenTheCacheIsMissing()
    {
        Manifest("java", """{ "name": "java", "paths": ["%APPDIR%"] }""");
        Dirs("java/1.0.0");

        Assert.False(definitions.IsScanned());
        Assert.Contains(Path.Combine(root, "java", "1.0.0"), TestTools.CaptureConsole(() => Manager().GetEnv(Shell.Bash)));
        Assert.True(definitions.IsScanned());
    }

    [Fact]
    public void ApplicationWithoutVersions_IsListedAndShown()
    {
        Manifest("java", """{ "name": "java", "paths": ["%APPDIR%"] }""");
        var tools = new ToolSettingsProvider(storage, definitions);
        var manager = new ToolManager(Options.Create(new Settings { RootPath = root }), definitions, tools, null);

        var row = TestTools.CaptureConsole(() => manager.List(false)).Split('\n').Single(i => i.StartsWith("java "));
        var details = TestTools.CaptureConsole(() => manager.Show("java"));
        var error = Assert.Throws<InvalidOperationException>(() => manager.Select("java", "latest"));

        Assert.Contains("not installed", row);
        Assert.Contains(Path.Combine(root, "java"), details);
        Assert.Contains("none, `app install java` installs one", details);
        Assert.Contains("has no installed versions", error.Message);
        Assert.Empty(tools.GetApplications());

        Dirs("java/1.0.0");
        definitions.Scan(root);

        Assert.Equal("latest", tools.GetSettings("java").Version);
    }

    [Fact]
    public void Env_DoesNotFailWhenTheScanFails()
    {
        Manifest("java", """{ "name": "java", "paths": ["%APPDIR%"] }""");
        Dirs("java/1.0.0");
        Manifest("broken", "{ not json");

        var output = "";
        var error = TestTools.CaptureError(() => output = TestTools.CaptureConsole(() => Manager().GetEnv(Shell.Bash)));

        Assert.Contains("Could not scan", error);
        Assert.Contains("export DT_PATH=", output);
    }

    [Fact]
    public void Scan_ListsDirectoriesWithoutManifestAsUnmanaged()
    {
        Manifest("java", """{ "name": "java", "paths": ["%APPDIR%"] }""");
        Manifest("group/inner", """{ "name": "inner", "flat": true }""");
        Dirs("java/1.0.0", "android-studio", ".git");
        var manager = Manager();
        manager.Scan();

        var list = TestTools.CaptureConsole(() => manager.List(false));

        Assert.Equal(new[] { "java" }, definitions.GetNames());
        Assert.Equal(new[] { "android-studio", "group" }, definitions.GetUnmanaged());
        Assert.Matches(@"(?m)^android-studio\s+-\s+-\s+-\s+unmanaged\s*$", list);
        Assert.Matches(@"(?m)^java\s+latest \(1\.0\.0\)\s+1\.0\.0\s+local\s*$", list);
        Assert.DoesNotContain(".git", list);
        Assert.Contains("unmanaged", TestTools.CaptureConsole(() => manager.Show("android-studio")));
    }

    [Fact]
    public void Scan_RecordsTheRepositoryOfFetchedApplications()
    {
        Manifest("age", """{ "name": "age", "paths": ["%APPDIR%"], "origin": { "repository": "owner/apps", "sha256": "x" } }""");
        Manifest("go", """{ "name": "go", "paths": ["%APPDIR%"] }""");
        Dirs("go/1.0.0");
        var tools = new ToolSettingsProvider(storage, definitions);
        var manager = Manager(tools);

        manager.Scan();

        Assert.Equal("owner/apps", tools.GetSettings("age").Source);
        Assert.Equal("latest", tools.GetSettings("age").Version);
        Assert.Null(tools.GetSettings("go").Source);
        Assert.Matches(@"(?m)^age\s+latest\s+repo\s+not installed\s*$", TestTools.CaptureConsole(() => manager.List(false)));

        Manifest("age", """{ "name": "age", "paths": ["%APPDIR%"] }""");
        manager.Scan();

        Assert.Null(tools.GetSettings("age").Source);
    }

    [Fact]
    public void ApplicationFromARepositoryThatIsGone_IsListedAndKeptByPrune()
    {
        Dirs("java");
        var tools = new ToolSettingsProvider(storage, definitions);
        tools.UpdateSettings("age", s => { s.Source = "owner/apps"; s.Version = "latest"; });
        var manager = Manager(tools);

        TestTools.CaptureConsole(manager.Prune);

        Assert.Matches(@"(?m)^age\s+latest\s+repo\s+not installed\s*$", TestTools.CaptureConsole(() => manager.List(false)));
        Assert.Contains("`app restore`", TestTools.CaptureConsole(() => manager.Show("age")));
        Assert.Equal("owner/apps", tools.GetSettings("age").Source);
    }

    [Fact]
    public void CacheOfAnEarlierVersion_IsScannedAgain()
    {
        Manifest("java", """{ "name": "java", "paths": ["%APPDIR%"] }""");
        Dirs("java/1.0.0");
        // An earlier cache was a dictionary of versions, read as a result without apps.
        storage.Save(TestTools.CacheFile, new ScanResult());

        Assert.False(definitions.IsScanned());
        Assert.Contains(Path.Combine(root, "java", "1.0.0"), TestTools.CaptureConsole(() => Manager().GetEnv(Shell.Bash)));
    }
}
