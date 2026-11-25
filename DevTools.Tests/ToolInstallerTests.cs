using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using DevTools.Catalog;
using DevTools.Infrastructure;
using DevTools.Installation;
using DevTools.Installation.Models;
using DevTools.Management;
using DevTools.Sources;
using DevTools.Sources.Models;
using Microsoft.Extensions.Options;
using Xunit;

namespace DevTools.Tests;

public sealed class ToolInstallerTests : IDisposable
{
    private class FakeVersionSource : IVersionSource
    {
        public List<RemoteVersion> Versions { get; } = new();
        public VersionSource LastSource { get; private set; }
        public int Calls { get; private set; }

        public Task<List<RemoteVersion>> GetVersionsAsync(VersionSource source)
        {
            Calls++;
            LastSource = source;
            return Task.FromResult(Versions);
        }
    }

    private class FakeDownloader : IDownloader
    {
        public Dictionary<string, byte[]> Files { get; } = new();
        public List<string> Urls { get; } = new();

        public Task DownloadAsync(string url, string fileName)
        {
            Urls.Add(url);
            File.WriteAllBytes(fileName, Files[url]);
            return Task.CompletedTask;
        }
    }

    private const string Url = "https://example.test/app.zip";

    private readonly string root = Directory.CreateTempSubdirectory("devtools-tests-").FullName;
    private readonly MemoryStorage storage = new();
    private readonly FakeVersionSource versions = new();
    private readonly FakeDownloader downloader = new();
    private readonly ToolDefinitionProvider definitions;
    private readonly ToolSettingsProvider settings;
    private readonly ToolManager manager;
    private readonly string appDir;
    private Platform platform = new("windows", "x64");

    public ToolInstallerTests()
    {
        definitions = new ToolDefinitionProvider(storage);
        settings = new ToolSettingsProvider(storage, definitions);
        manager = new ToolManager(Options.Create(new Settings { RootPath = root }), definitions, settings, new Lazy<IToolInstaller>(() => new ToolInstaller(storage, versions, downloader, platform)));
        appDir = Path.Combine(root, "app");
        Directory.CreateDirectory(appDir);
        File.WriteAllText(Path.Combine(appDir, ".app"), """{ "name": "app", "paths": ["%APPDIR%"] }""");
    }

    public void Dispose()
    {
        Directory.Delete(root, true);
    }

    private void Source(string steps)
    {
        SetInstall($$"""{ "versions": { "type": "github-releases", "repo": "a/b" }, "steps": {{steps}} }""");
    }

    // Puts the install section into .app of the application.
    private void SetInstall(string install)
    {
        var fileName = Path.Combine(appDir, ".app");
        var manifest = JsonNode.Parse(File.ReadAllText(fileName))!.AsObject();
        manifest["install"] = JsonNode.Parse(install);
        File.WriteAllText(fileName, manifest.ToJsonString());
    }

    private void Remote(string version, params (string Asset, string Url, byte[] Content)[] assets)
    {
        versions.Versions.Insert(0, new RemoteVersion(version, "v" + version, assets.Select(i => new RemoteAsset(i.Asset, i.Url, null)).ToList()));
        foreach (var asset in assets)
            downloader.Files[asset.Url] = asset.Content;
    }

    private void Remote(string version, byte[] content, string digest = null)
    {
        versions.Versions.Insert(0, new RemoteVersion(version, "v" + version, new[] { new RemoteAsset($"app-{version}.zip", Url, digest) }));
        downloader.Files[Url] = content;
    }

    private static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(content);
            }
        }
        return stream.ToArray();
    }

    private static byte[] TarGz(params (string Name, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var gzip = new GZipStream(stream, CompressionMode.Compress, true))
        using (var writer = new TarWriter(gzip))
        {
            foreach (var (name, content) in entries)
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)) });
        }
        return stream.ToArray();
    }

    private string Installed(params string[] path)
    {
        return Path.Combine(new[] { appDir }.Concat(path).ToArray());
    }

    // A junction on Windows needs no extra privileges, unlike a symbolic link.
    private static void LinkDirectory(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }

        using var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd", $"/c mklink /J \"{link}\" \"{target}\"") { RedirectStandardOutput = true });
        mklink.WaitForExit();
    }

    private void Install(string version = "latest")
    {
        TestTools.CaptureConsole(() => manager.Install("app", version));
    }

    private void AssertNoWorkDirectories()
    {
        Assert.Empty(Directory.GetDirectories(appDir, ".install-*"));
    }

    [Fact]
    public void Install_LatestStripsRootAndRegistersVersion()
    {
        Source("""[ { "type": "download", "asset": "app-{version}.zip" }, { "type": "extract", "stripRoot": true } ]""");
        Remote("1.0.0", Zip(("app-1.0.0/bin/app.txt", "old")));
        Remote("2.0.0", Zip(("app-2.0.0/bin/app.txt", "new")));

        Install();

        Assert.Equal("new", File.ReadAllText(Installed("2.0.0", "bin", "app.txt")));
        Assert.False(Directory.Exists(Installed("1.0.0")));
        Assert.False(File.Exists(Installed("2.0.0", "app-2.0.0.zip")));
        Assert.Equal("2.0.0", definitions.GetVersion("app", "latest").Manifest.Version);
        AssertNoWorkDirectories();
    }

    [Fact]
    public void Install_ExtractsTarGzFromUrlWithPlaceholders()
    {
        var url = "https://example.test/v1.0.0/app-1.0.0.tar.gz";
        Source("""[ { "type": "download", "url": "https://example.test/{tag}/app-{version}.tar.gz" }, { "type": "extract" } ]""");
        versions.Versions.Add(new RemoteVersion("1.0.0", "v1.0.0", new RemoteAsset[0]));
        downloader.Files[url] = TarGz(("app.txt", "hello"));

        Install("1.0.0");

        Assert.Equal("hello", File.ReadAllText(Installed("1.0.0", "app.txt")));
    }

    [Fact]
    public void Install_MoveRenamesSingleFile()
    {
        Source("""[ { "type": "download", "asset": "app-{version}.zip", "to": "app_{version}.jar" }, { "type": "move", "from": "app_{version}.jar", "to": "lib/app.jar" } ]""");
        Remote("1.0.0", Encoding.UTF8.GetBytes("jar"));

        Install();

        Assert.Equal("jar", File.ReadAllText(Installed("1.0.0", "lib", "app.jar")));
    }

    [Fact]
    public void Install_AlreadyInstalledDoesNotDownload()
    {
        Source("""[ { "type": "download", "asset": "app-{version}.zip", "to": "a.bin" } ]""");
        Remote("1.0.0", new byte[] { 1 });
        Directory.CreateDirectory(Installed("1.0.0"));

        var output = TestTools.CaptureConsole(() => manager.Install("app", "1.0.0"));

        Assert.Empty(downloader.Urls);
        Assert.Contains("already installed", output);
    }

    [Fact]
    public void Install_ReportsSelectionAfterInstall()
    {
        Source("""[ { "type": "download", "asset": "app-{version}.zip", "to": "a.bin" } ]""");
        Directory.CreateDirectory(Installed("1.0.0"));
        definitions.Scan(root);
        settings.UpdateSettings("app", s => s.Version = "1.0.0");
        Remote("2.0.0", new byte[] { 1 });

        var pinned = TestTools.CaptureConsole(() => manager.Install("app", "2.0.0"));

        Assert.Contains("still uses 1.0.0", pinned);
        Assert.Equal("1.0.0", settings.GetSettings("app").Version);

        settings.UpdateSettings("app", s => s.Version = "latest");
        Remote("3.0.0", new byte[] { 1 });

        Assert.Contains("now uses 3.0.0", TestTools.CaptureConsole(() => manager.Install("app", "3.0.0")));
    }

    [Fact]
    public void Install_UnknownVersionThrows()
    {
        Source("""[ { "type": "download", "asset": "app-{version}.zip" } ]""");
        Remote("1.0.0", new byte[] { 1 });

        var error = Assert.Throws<InvalidOperationException>(() => Install("9.9.9"));

        Assert.Contains("9.9.9", error.Message);
    }

    [Fact]
    public void Install_WithoutSourceThrows()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Install());

        Assert.Contains("install section", error.Message);
    }

    [Fact]
    public void Install_FlatApplicationThrows()
    {
        File.WriteAllText(Path.Combine(appDir, ".app"), """{ "name": "app", "flat": true }""");
        Source("""[ { "type": "download", "asset": "a" } ]""");

        var error = Assert.Throws<InvalidOperationException>(() => Install());

        Assert.Contains("is flat", error.Message);
    }

    [Fact]
    public void Install_MatchingDigestSucceeds()
    {
        var content = new byte[] { 1, 2, 3 };
        Source("""[ { "type": "download", "asset": "app-{version}.zip", "to": "a.bin" } ]""");
        Remote("1.0.0", content, "sha256:" + Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant());

        Install();

        Assert.True(File.Exists(Installed("1.0.0", "a.bin")));
    }

    [Fact]
    public void Install_DigestMismatchFailsAndCleansUp()
    {
        Source("""[ { "type": "download", "asset": "app-{version}.zip", "to": "a.bin" } ]""");
        Remote("1.0.0", new byte[] { 1 }, "sha256:00");

        var error = Assert.Throws<InvalidOperationException>(() => Install());

        Assert.Contains("SHA-256", error.Message);
        Assert.False(Directory.Exists(Installed("1.0.0")));
        AssertNoWorkDirectories();
    }

    [Theory]
    [InlineData("""[ { "type": "download", "asset": "app-{version}.zip", "to": "../../evil.bin" } ]""")]
    [InlineData("""[ { "type": "download", "asset": "app-{version}.zip", "to": "a.bin" }, { "type": "move", "from": "a.bin", "to": "../evil.bin" } ]""")]
    [InlineData("""[ { "type": "download", "asset": "app-{version}.zip", "to": "/evil.bin" } ]""")]
    [InlineData("""[ { "type": "download", "asset": "app-{version}.zip", "to": "..\\..\\evil.bin" } ]""")]
    [InlineData("""[ { "type": "download", "asset": "app-{version}.zip", "to": "a.bin" }, { "type": "move", "from": "a.bin", "to": "..\\evil.bin" } ]""")]
    [InlineData("""[ { "type": "download", "asset": "app-{version}.zip", "to": "\\evil.bin" } ]""")]
    [InlineData("""[ { "type": "download", "asset": "app-{version}.zip", "to": "C:\\evil.bin" } ]""")]
    [InlineData("""[ { "type": "download", "asset": "app-{version}.zip", "to": "C:evil.bin" } ]""")]
    public void Install_PathsOutsideWorkDirectoryAreRejected(string steps)
    {
        Source(steps);
        Remote("1.0.0", new byte[] { 1 });

        var error = Assert.Throws<InvalidOperationException>(() => Install());

        Assert.Contains("outside", error.Message);
        AssertNoWorkDirectories();
    }

    [Fact]
    public void Install_DownloadedFileIsExecutable()
    {
        Source("""[ { "type": "download", "asset": "app-{version}.zip", "to": "bin\\tool" } ]""");
        Remote("1.0.0", new byte[] { 1 });

        Install();

        TestTools.AssertExecutable(Installed("1.0.0", "bin", "tool"));
    }

    [Fact]
    public void Install_ZipEntryOutsideDestinationIsRejected()
    {
        Source("""[ { "type": "download", "asset": "app-{version}.zip" }, { "type": "extract" } ]""");
        Remote("1.0.0", Zip(("../../../evil.txt", "evil")));

        Assert.ThrowsAny<IOException>(() => Install());

        Assert.False(File.Exists(Path.Combine(root, "evil.txt")));
        Assert.False(File.Exists(Path.Combine(appDir, "evil.txt")));
        AssertNoWorkDirectories();
    }

    private void RemoteAssets(string version, params string[] names)
    {
        var assets = names.Select(i => new RemoteAsset(i, $"https://example.test/{i}", null)).ToArray();
        versions.Versions.Add(new RemoteVersion(version, "v" + version, assets));
        foreach (var name in names)
            downloader.Files[$"https://example.test/{name}"] = Encoding.UTF8.GetBytes(name);
    }

    [Fact]
    public void Install_AssetWildcardSelectsTheOnlyMatchAndKeepsItsName()
    {
        Source("""[ { "type": "download", "asset": "app.{version}.??????.bin" } ]""");
        RemoteAssets("1.0.0", "app.1.0.0.fa48d0.bin", "app.1.0.0.fa48d0_symbols.bin", "app.1.0.0.fa48d0.bin.sha256");

        Install();

        Assert.Equal("app.1.0.0.fa48d0.bin", File.ReadAllText(Installed("1.0.0", "app.1.0.0.fa48d0.bin")));
    }

    [Fact]
    public void Install_AmbiguousAssetWildcardFailsAndListsMatches()
    {
        Source("""[ { "type": "download", "asset": "app-{version}-*.bin" } ]""");
        RemoteAssets("1.0.0", "app-1.0.0-a.bin", "app-1.0.0-b.bin");

        var error = Assert.Throws<InvalidOperationException>(() => Install());

        Assert.Contains("app-1.0.0-a.bin", error.Message);
        Assert.Contains("app-1.0.0-b.bin", error.Message);
        Assert.Empty(downloader.Urls);
        AssertNoWorkDirectories();
    }

    [Fact]
    public void Install_AssetWithoutMatchFails()
    {
        Source("""[ { "type": "download", "asset": "app-{version}-*.zip" } ]""");
        RemoteAssets("1.0.0", "app-1.0.0-a.bin");

        var error = Assert.Throws<InvalidOperationException>(() => Install());

        Assert.Contains("no asset", error.Message);
    }

    [Fact]
    public void Install_AssetNameIsCaseSensitive()
    {
        Source("""[ { "type": "download", "asset": "APP-{version}.bin" } ]""");
        RemoteAssets("1.0.0", "app-1.0.0.bin");

        Assert.Throws<InvalidOperationException>(() => Install());
    }

    private void Update()
    {
        Source("""[ { "type": "download", "asset": "app-{version}.zip", "to": "a.bin" } ]""");
        TestTools.CaptureConsole(() => manager.Update("app"));
    }

    [Fact]
    public void Update_InstallsNewestWhenNewerThanInstalled()
    {
        Directory.CreateDirectory(Installed("1.9.0"));
        Remote("1.9.0", new byte[] { 1 });
        Remote("1.10.0", new byte[] { 1 });

        Update();

        Assert.True(File.Exists(Installed("1.10.0", "a.bin")));
        Assert.Equal("1.10.0", definitions.GetVersion("app", "latest").Manifest.Version);
    }

    [Fact]
    public void Update_InstallsNewestWhenNothingIsInstalled()
    {
        Remote("1.0.0", new byte[] { 1 });

        Update();

        Assert.True(File.Exists(Installed("1.0.0", "a.bin")));
    }

    [Fact]
    public void Update_DoesNothingWhenNewestIsInstalled()
    {
        Directory.CreateDirectory(Installed("2.0.0"));
        Remote("2.0.0", new byte[] { 1 });

        Update();

        Assert.Empty(downloader.Urls);
    }

    [Fact]
    public void Update_DoesNotInstallOlderThanInstalled()
    {
        Directory.CreateDirectory(Installed("12.2.0"));
        Remote("12.1.4", new byte[] { 1 });

        Update();

        Assert.Empty(downloader.Urls);
        Assert.False(Directory.Exists(Installed("12.1.4")));
    }

    [Fact]
    public void Update_WithoutAvailableVersionsThrows()
    {
        var error = Assert.Throws<InvalidOperationException>(Update);

        Assert.Contains("No versions", error.Message);
    }

    [Fact]
    public void Update_KeepsSelectedVersion()
    {
        Directory.CreateDirectory(Installed("1.0.0"));
        definitions.Scan(root);
        settings.UpdateSettings("app", s => s.Version = "1.0.0");
        Remote("2.0.0", new byte[] { 1 });

        Update();

        Assert.Equal("1.0.0", settings.GetSettings("app").Version);
    }

    private void Remove(string version)
    {
        TestTools.CaptureConsole(() => manager.Remove("app", version));
    }

    private string InstalledFile(string version)
    {
        var file = Installed(version, "bin", "a.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        File.WriteAllText(file, version);
        return file;
    }

    [Fact]
    public void Remove_DeletesVersionAndKeepsTheRest()
    {
        InstalledFile("1.0.0");
        InstalledFile("2.0.0");
        Source("""[ { "type": "download", "asset": "a" } ]""");

        Remove("1.0.0");

        Assert.False(Directory.Exists(Installed("1.0.0")));
        Assert.True(Directory.Exists(Installed("2.0.0")));
        Assert.True(File.Exists(Path.Combine(appDir, ".app")));
        Assert.Contains("install", File.ReadAllText(Path.Combine(appDir, ".app")));
        Assert.Equal(new[] { "2.0.0" }, definitions.GetVersions("app").Select(i => i.Manifest.Version));
        Assert.Empty(Directory.GetDirectories(appDir, ".remove-*"));
    }

    [Fact]
    public void Remove_LatestSelectionFollowsToPreviousVersion()
    {
        InstalledFile("1.0.0");
        InstalledFile("2.0.0");
        definitions.Scan(root);
        settings.UpdateSettings("app", s => s.Version = "latest");

        Remove("2.0.0");

        Assert.Equal("latest", settings.GetSettings("app").Version);
        Assert.Equal("1.0.0", definitions.GetVersion("app", "latest").Manifest.Version);
    }

    [Fact]
    public void Remove_LastVersionKeepsApplicationDirectory()
    {
        InstalledFile("1.0.0");

        Remove("1.0.0");

        Assert.False(Directory.Exists(Installed("1.0.0")));
        Assert.True(File.Exists(Path.Combine(appDir, ".app")));
        Assert.Empty(definitions.GetVersions("app"));
    }

    [Fact]
    public void Remove_SelectedVersionIsRefused()
    {
        var file = InstalledFile("1.0.0");
        InstalledFile("2.0.0");
        definitions.Scan(root);
        settings.UpdateSettings("app", s => s.Version = "1.0.0");

        var error = Assert.Throws<InvalidOperationException>(() => Remove("1.0.0"));

        Assert.Contains("is selected", error.Message);
        Assert.True(File.Exists(file));
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("oldest")]
    [InlineData("3.0.0")]
    [InlineData("..")]
    public void Remove_OnlyExactInstalledVersionIsAccepted(string version)
    {
        var file = InstalledFile("1.0.0");

        var error = Assert.Throws<InvalidOperationException>(() => Remove(version));

        Assert.Contains("not found", error.Message);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Remove_FlatApplicationIsRefused()
    {
        var directory = Path.Combine(root, "single");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ".app"), """{ "name": "single", "flat": true }""");

        var error = Assert.Throws<InvalidOperationException>(() => manager.Remove("single", "default"));

        Assert.Contains("is flat", error.Message);
        Assert.True(File.Exists(Path.Combine(directory, ".app")));
    }

    [Fact]
    public void Remove_DeletesReadOnlyFiles()
    {
        var file = InstalledFile("1.0.0");
        File.SetAttributes(file, FileAttributes.ReadOnly);

        Remove("1.0.0");

        Assert.False(Directory.Exists(Installed("1.0.0")));
    }

    [Fact]
    public void Remove_DoesNotFollowLinks()
    {
        var outside = Directory.CreateTempSubdirectory("devtools-tests-outside-").FullName;
        try
        {
            var kept = Path.Combine(outside, "kept.txt");
            File.WriteAllText(kept, "kept");
            InstalledFile("1.0.0");
            LinkDirectory(Installed("1.0.0", "link"), outside);
            Assert.True(File.Exists(Installed("1.0.0", "link", "kept.txt")));

            Remove("1.0.0");

            Assert.False(Directory.Exists(Installed("1.0.0")));
            Assert.True(File.Exists(kept));
        }
        finally
        {
            Directory.Delete(outside, true);
        }
    }

    // Archives built with 7-Zip: root/bin/app.txt, root/empty.txt and root/emptydir.
    private const string SevenZipNormal = "N3q8ryccAATvsKLmhAAAAAAAAAAhAAAAAAAAACz49QEBAARoZWxsbwAAAIEzB64P4OxyY2LTejR4Zaj6VcxzqN/ldRyFArIRgoRbvi9vAbHbjhx7PRc2XnP65OlL7vQkAbvFYoKYnnvtpZWMmj4o6Ynx4RWQLCOz/Q/XsO4q4JOPtAUe/PF/rvrVm3kmVT9+haEGhRnIiGNULOKsnJ85tI8eZAAXBgkBCXsABwsBAAEjAwEBBV0AEAAADIDyCgH5tVmKAAA=";

    // A solid archive with a/one.txt, a/two.txt and a/three.txt.
    private const string SevenZipSolid = "N3q8ryccAAQWSo5VfwAAAAAAAAAhAAAAAAAAAH0i6e4BAApvbmV0aHJlZXR3bwAAAIEzB64Pzt0HYQkqrtOqhSYoGKffAUXJhZCOpS4NhLmsJMtDZ5jrkYntIEgfsl260gexiFg6Egz1jDXIVlsYx3WUvQ3UiRaTSAvMJGc4v8VA2KwQ2hYKOLu2xrT9WMRLvzTCyoGuBap6rkbGOGkAFwYPAQlwAAcLAQABIwMBAQVdABAAAAyAvgoBAxvsbwAA";

    // An archive with a file named ../../evil.txt.
    private const string SevenZipTraversal = "N3q8ryccAAQA2a+HCAAAAAAAAACSAAAAAAAAAGMXvRYBAANldmlsAAEEBgABCQgABwsBAAEhIQEADAQACAoBUjH7jQAABQMOAcAZCQAAAAAAAAAAABExAHgAeAAAAHgAeAAvAHgAeAAAAC4ALgAvAC4ALgAvAGUAdgBpAGwALgB0AHgAdAAAABkCAAAUGgEAqsUKjQtt3QGqxQqNC23dAarFCo0Lbd0BFQ4BABAAAAAQAAAAIAAAAAAA";

    private void SevenZipRemote(string base64)
    {
        versions.Versions.Add(new RemoteVersion("1.0.0", "v1.0.0", new[] { new RemoteAsset("app.7z", Url, null) }));
        downloader.Files[Url] = Convert.FromBase64String(base64);
    }

    [Fact]
    public void Install_Extracts7zWithStripRoot()
    {
        Source("""[ { "type": "download", "asset": "app.7z" }, { "type": "extract", "stripRoot": true } ]""");
        SevenZipRemote(SevenZipNormal);

        Install();

        Assert.Equal("hello", File.ReadAllText(Installed("1.0.0", "bin", "app.txt")));
        TestTools.AssertExecutable(Installed("1.0.0", "bin", "app.txt"));
        Assert.Equal(0, new FileInfo(Installed("1.0.0", "empty.txt")).Length);
        Assert.True(Directory.Exists(Installed("1.0.0", "emptydir")));
        Assert.False(File.Exists(Installed("1.0.0", "app.7z")));
        AssertNoWorkDirectories();
    }

    [Fact]
    public void Install_ExtractsSolid7z()
    {
        Source("""[ { "type": "download", "asset": "app.7z" }, { "type": "extract" } ]""");
        SevenZipRemote(SevenZipSolid);

        Install();

        Assert.Equal("one", File.ReadAllText(Installed("1.0.0", "a", "one.txt")));
        Assert.Equal("two", File.ReadAllText(Installed("1.0.0", "a", "two.txt")));
        Assert.Equal("three", File.ReadAllText(Installed("1.0.0", "a", "three.txt")));
    }

    [Fact]
    public void Install_7zWithPathOutsideDirectoryFails()
    {
        Source("""[ { "type": "download", "asset": "app.7z" }, { "type": "extract" } ]""");
        SevenZipRemote(SevenZipTraversal);

        var error = Assert.Throws<InvalidOperationException>(() => Install());

        Assert.Contains("outside", error.Message);
        Assert.False(File.Exists(Path.Combine(appDir, "evil.txt")));
        Assert.False(File.Exists(Path.Combine(root, "evil.txt")));
        Assert.False(Directory.Exists(Installed("1.0.0")));
        AssertNoWorkDirectories();
    }

    [Fact]
    public void Install_Corrupted7zFailsAndLeavesNothing()
    {
        Source("""[ { "type": "download", "asset": "app.7z" }, { "type": "extract" } ]""");
        SevenZipRemote(Convert.ToBase64String(new byte[] { 1, 2, 3, 4 }));

        Assert.ThrowsAny<Exception>(() => Install());

        Assert.False(Directory.Exists(Installed("1.0.0")));
        AssertNoWorkDirectories();
    }

    private void RemoteTwoPart()
    {
        Source("""[ { "type": "download", "asset": "app-{version}.zip" } ]""");
        versions.Versions.Add(new RemoteVersion("12.2.0", "v12.2", new[] { new RemoteAsset("app-12.2.zip", Url, null) }, "12.2"));
        downloader.Files[Url] = new byte[] { 1 };
    }

    [Fact]
    public void Install_NormalizedVersionUsesSourceFormInPlaceholdersAndThreePartsInDirectory()
    {
        RemoteTwoPart();

        Install();

        Assert.True(File.Exists(Installed("12.2.0", "app-12.2.zip")));
        Assert.False(Directory.Exists(Installed("12.2")));
    }

    [Theory]
    [InlineData("12.2")]
    [InlineData("12.2.0")]
    public void Install_AcceptsVersionInEitherForm(string version)
    {
        RemoteTwoPart();

        Install(version);

        Assert.True(Directory.Exists(Installed("12.2.0")));
    }

    [Fact]
    public void Install_LatestDoesNothingWhenTheSameVersionIsInstalledInThreeParts()
    {
        Directory.CreateDirectory(Installed("12.2.0"));
        RemoteTwoPart();

        Install();

        Assert.Empty(downloader.Urls);
    }

    [Fact]
    public void Install_TwoPartRemoteDoesNothingWhenInstalledInThreeParts()
    {
        Directory.CreateDirectory(Installed("81.0.0"));
        Source("""[ { "type": "download", "asset": "app-{version}.zip" } ]""");
        Remote("81.0", new byte[] { 1 });

        Install("81.0");

        Assert.Empty(downloader.Urls);
        Assert.False(Directory.Exists(Installed("81.0")));
    }

    [Theory]
    [InlineData("103.0.0", "103.0")]
    [InlineData("103.0", "103.0.0")]
    public void Update_TreatsBothFormsAsTheSameVersion(string installed, string remote)
    {
        Directory.CreateDirectory(Installed(installed));
        Remote(remote, new byte[] { 1 });

        Update();

        Assert.Empty(downloader.Urls);
    }

    [Fact]
    public void Update_TwoPartRemoteNewerThanThreePartInstalledIsInstalled()
    {
        Directory.CreateDirectory(Installed("81.0.0"));
        Remote("103.0", new byte[] { 1 });

        Update();

        Assert.True(Directory.Exists(Installed("103.0")));
    }

    [Fact]
    public void Remove_AcceptsVersionInEitherForm()
    {
        InstalledFile("1.2.0");
        InstalledFile("2.0.0");

        Remove("1.2");

        Assert.False(Directory.Exists(Installed("1.2.0")));
    }

    [Fact]
    public void Remove_SelectedVersionIsRefusedInEitherForm()
    {
        InstalledFile("1.2.0");
        InstalledFile("2.0.0");
        definitions.Scan(root);
        settings.UpdateSettings("app", s => s.Version = "1.2.0");

        Assert.Throws<InvalidOperationException>(() => Remove("1.2"));

        Assert.True(Directory.Exists(Installed("1.2.0")));
    }

    [Fact]
    public void Select_StoresInstalledNameOfTheVersion()
    {
        Directory.CreateDirectory(Installed("1.2.0"));
        definitions.Scan(root);

        TestTools.CaptureConsole(() => manager.Select("app", "1.2"));

        Assert.Equal("1.2.0", settings.GetSettings("app").Version);
    }

    [Fact]
    public void Install_NonHttpsUrlIsRejected()
    {
        Source("""[ { "type": "download", "url": "http://example.test/a.zip" } ]""");
        versions.Versions.Add(new RemoteVersion("1.0.0", "v1.0.0", new RemoteAsset[0]));

        var error = Assert.Throws<InvalidOperationException>(() => Install());

        Assert.Contains("https", error.Message);
    }

    [Fact]
    public void Install_StripRootWithoutSingleRootFails()
    {
        Source("""[ { "type": "download", "asset": "app-{version}.zip" }, { "type": "extract", "stripRoot": true } ]""");
        Remote("1.0.0", Zip(("a/x.txt", "1"), ("b/y.txt", "2")));

        var error = Assert.Throws<InvalidOperationException>(() => Install());

        Assert.Contains("stripRoot", error.Message);
        AssertNoWorkDirectories();
    }

    [Fact]
    public void Install_UnknownStepTypeFails()
    {
        Source("""[ { "type": "run", "command": "x" } ]""");
        Remote("1.0.0", new byte[] { 1 });

        Assert.ThrowsAny<Exception>(() => Install());

        Assert.False(Directory.Exists(Installed("1.0.0")));
    }

    [Fact]
    public void Install_ExpandsOsAndArchWithProjectNames()
    {
        platform = new Platform("linux", "x64");
        SetInstall("""
            {
              "versions": { "type": "github-releases", "repo": "a/b" },
              "archNames": { "x64": "amd64" },
              "steps": [ { "type": "download", "asset": "app-{version}-{os}-{arch}.tar.gz" }, { "type": "extract" } ]
            }
            """);
        Remote("1.0.0",
            ("app-1.0.0-windows-amd64.tar.gz", "https://example.test/windows", TarGz(("app.txt", "windows"))),
            ("app-1.0.0-linux-amd64.tar.gz", "https://example.test/linux", TarGz(("app.txt", "linux"))));

        Install();

        Assert.Equal("linux", File.ReadAllText(Installed("1.0.0", "app.txt")));
    }

    [Theory]
    [InlineData("windows", "x64", "windows.zip")]
    [InlineData("linux", "x64", "linux-x64.tar.gz")]
    [InlineData("linux", "arm64", "linux-arm64.tar.gz")]
    public void Install_RunsOnlyStepsOfThePlatform(string os, string arch, string expected)
    {
        platform = new Platform(os, arch);
        Source("""
            [
              { "type": "download", "os": "windows", "asset": "windows.zip" },
              { "type": "download", "os": "linux", "arch": "x64", "asset": "linux-x64.tar.gz" },
              { "type": "download", "os": "linux", "arch": "arm64", "asset": "linux-arm64.tar.gz" },
              { "type": "extract" }
            ]
            """);
        Remote("1.0.0",
            ("windows.zip", "https://example.test/w", Zip(("app.txt", "windows.zip"))),
            ("linux-x64.tar.gz", "https://example.test/lx", TarGz(("app.txt", "linux-x64.tar.gz"))),
            ("linux-arm64.tar.gz", "https://example.test/la", TarGz(("app.txt", "linux-arm64.tar.gz"))));

        Install();

        Assert.Equal(expected, File.ReadAllText(Installed("1.0.0", "app.txt")));
        Assert.Single(downloader.Urls);
    }

    [Fact]
    public void Install_FailsWhenNoStepRunsOnThePlatform()
    {
        platform = new Platform("linux", "x64");
        Source("""[ { "type": "download", "os": "windows", "asset": "app.zip" } ]""");
        Remote("1.0.0", ("app.zip", Url, Zip(("app.txt", "x"))));

        var error = Assert.Throws<InvalidOperationException>(() => Install());

        Assert.Contains("no install steps for linux x64", error.Message);
        Assert.Empty(downloader.Urls);
        Assert.False(Directory.Exists(Installed("1.0.0")));
    }

    [Fact]
    public void Update_ExpandsOsAndArchInVersionSource()
    {
        platform = new Platform("linux", "arm64");
        SetInstall("""
            {
              "versions": { "type": "web-page", "url": "https://example.test/{os}/", "pattern": "app-(?<version>\\d+)-{os}-{arch}\\.tar\\.gz" },
              "osNames": { "linux": "Linux" },
              "steps": [ { "type": "download", "asset": "*" } ]
            }
            """);

        Assert.Throws<InvalidOperationException>(() => TestTools.CaptureConsole(() => manager.Update("app")));

        Assert.Equal("https://example.test/Linux/", versions.LastSource.Url);
        Assert.Equal(@"app-(?<version>\d+)-Linux-arm64\.tar\.gz", versions.LastSource.Pattern);
    }

    [Fact]
    public void Install_FetchesManifestsOfUnknownApplicationFromSources()
    {
        var repo = Directory.CreateDirectory(Path.Combine(root, "..", Path.GetFileName(root) + "-repo", "apps")).Parent!.FullName;
        try
        {
            File.WriteAllText(Path.Combine(repo, "apps", "tool.app"), """
                { "name": "tool", "paths": ["%APPDIR%"],
                  "install": { "versions": { "type": "github-releases", "repo": "a/b" }, "steps": [ { "type": "download", "asset": "tool.zip" }, { "type": "extract" } ] } }
                """);
            File.WriteAllText(Path.Combine(repo, "index.json"), """{ "name": "test apps", "maintainer": "someone" }""");
            storage.Save("config/sources.json", new SourcesState { Repositories = new List<string> { repo } });
            var options = Options.Create(new Settings { RootPath = root });
            var sources = new SourceManager(options, storage, definitions, new RepositoryClient(new System.Net.Http.HttpClient()));
            var withSources = new ToolManager(options, definitions, settings, new Lazy<IToolInstaller>(() => new ToolInstaller(storage, versions, downloader, platform)), new Lazy<ISourceManager>(sources));
            Remote("1.0.0", ("tool.zip", Url, Zip(("tool.txt", "fetched"))));

            TestTools.CaptureConsole(() => withSources.Install("tool", "latest"));

            Assert.Equal("fetched", File.ReadAllText(Path.Combine(root, "tool", "1.0.0", "tool.txt")));
            Assert.Contains("origin", File.ReadAllText(Path.Combine(root, "tool", ".app")));
            Assert.Equal(repo, settings.GetSettings("tool").Source);

            // The tools directory is lost, config/apps.json is enough to get the application back.
            Directory.Delete(Path.Combine(root, "tool"), true);
            withSources.Scan();
            var listed = TestTools.CaptureConsole(() => withSources.List(false));
            TestTools.CaptureConsole(() => withSources.Restore());

            Assert.Matches(@"(?m)^tool\s+latest\s+repo\s+not installed\s*$", listed);
            Assert.Equal("fetched", File.ReadAllText(Path.Combine(root, "tool", "1.0.0", "tool.txt")));
            Assert.Contains("is installed", TestTools.CaptureConsole(() => withSources.Restore()));
        }
        finally
        {
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public void UpdateAll_UpdatesEveryApplicationWithSourceAndReportsFailures()
    {
        Source("""[ { "type": "download", "asset": "app-{version}.zip" }, { "type": "extract" } ]""");
        Remote("1.0.0", Zip(("app.txt", "x")));
        Install();
        Remote("2.0.0", Zip(("app.txt", "y")));
        var broken = Path.Combine(root, "broken");
        Directory.CreateDirectory(Path.Combine(broken, "0.1.0"));
        File.WriteAllText(Path.Combine(broken, ".app"), """{ "name": "broken", "paths": ["%APPDIR%"], "install": { } }""");
        var plain = Path.Combine(root, "plain");
        Directory.CreateDirectory(plain);
        File.WriteAllText(Path.Combine(plain, ".app"), """{ "name": "plain", "flat": true, "paths": ["%APPDIR%"] }""");

        var output = new StringWriter();
        var original = Console.Out;
        Console.SetOut(output);
        string error;
        try
        {
            error = TestTools.CaptureError(() =>
                Assert.Contains("Could not update broken", Assert.Throws<InvalidOperationException>(() => manager.UpdateAll()).Message));
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.True(Directory.Exists(Installed("2.0.0")));
        Assert.Contains("broken: ", error);
        Assert.Contains("Updated 1, up to date 0, failed 1 of 2 application(s): app", output.ToString());
        Assert.DoesNotContain("plain", output + error);
    }

    [Fact]
    public async Task GetVersions_UsesCachedVersionsForAnHour()
    {
        Source("""[ { "type": "download", "asset": "app-{version}.zip" } ]""");
        Remote("1.0.0", Zip(("app.txt", "x")));
        var time = new FakeTime();
        var installer = new ToolInstaller(storage, versions, downloader, platform, new VersionCache(storage, time));

        await installer.GetVersionsAsync(appDir);
        await installer.GetVersionsAsync(appDir);
        Assert.Equal(1, versions.Calls);

        await installer.GetVersionsAsync(appDir, refresh: true);
        Assert.Equal(2, versions.Calls);

        time.Now += VersionCache.Duration;
        await installer.GetVersionsAsync(appDir);
        Assert.Equal(3, versions.Calls);
    }

    [Fact]
    public void Install_AsksAgainForAVersionNotInTheCache()
    {
        Source("""[ { "type": "download", "asset": "app-{version}.zip" }, { "type": "extract" } ]""");
        Remote("1.0.0", Zip(("app.txt", "x")));
        var cache = new VersionCache(storage, new FakeTime());
        var cached = new ToolManager(Options.Create(new Settings { RootPath = root }), definitions, settings,
            new Lazy<IToolInstaller>(() => new ToolInstaller(storage, versions, downloader, platform, cache)));
        TestTools.CaptureConsole(() => cached.Install("app", "1.0.0"));
        Remote("2.0.0", Zip(("app.txt", "y")));

        TestTools.CaptureConsole(() => cached.Install("app", "2.0.0"));

        Assert.True(Directory.Exists(Installed("2.0.0")));
    }
}
