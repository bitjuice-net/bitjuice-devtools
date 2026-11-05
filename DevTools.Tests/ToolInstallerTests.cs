using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using DevTools.App;
using Microsoft.Extensions.Options;
using Xunit;

namespace DevTools.Tests
{
    public sealed class ToolInstallerTests : IDisposable
    {
        private class FakeVersionSource : IVersionSource
        {
            public List<RemoteVersion> Versions { get; } = new();

            public Task<List<RemoteVersion>> GetVersionsAsync(VersionSource source)
            {
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

        public ToolInstallerTests()
        {
            definitions = new ToolDefinitionProvider(storage);
            settings = new ToolSettingsProvider(storage, definitions);
            manager = new ToolManager(Options.Create(new Settings { RootPath = root }), definitions, settings, new ToolInstaller(storage, versions, downloader));
            appDir = Path.Combine(root, "app");
            Directory.CreateDirectory(appDir);
            File.WriteAllText(Path.Combine(appDir, ".app"), """{ "name": "app", "versioned": true, "paths": ["%APPDIR%"] }""");
        }

        public void Dispose()
        {
            Directory.Delete(root, true);
        }

        private void Source(string steps)
        {
            File.WriteAllText(Path.Combine(appDir, ".source"), $$"""{ "versions": { "type": "github-releases", "repo": "a/b" }, "steps": {{steps}} }""");
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
            definitions.Discover(root);
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

            Assert.Contains(".source", error.Message);
        }

        [Fact]
        public void Install_UnversionedApplicationThrows()
        {
            Source("""[ { "type": "download", "asset": "a" } ]""");
            File.WriteAllText(Path.Combine(appDir, ".app"), """{ "name": "app", "version": "default" }""");

            var error = Assert.Throws<InvalidOperationException>(() => Install());

            Assert.Contains("versioned", error.Message);
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
        [InlineData("""[ { "type": "download", "asset": "app-{version}.zip", "to": "..\\..\\evil.bin" } ]""")]
        [InlineData("""[ { "type": "download", "asset": "app-{version}.zip", "to": "a.bin" }, { "type": "move", "from": "a.bin", "to": "..\\evil.bin" } ]""")]
        [InlineData("""[ { "type": "download", "asset": "app-{version}.zip", "to": "C:\\evil.bin" } ]""")]
        public void Install_PathsOutsideWorkDirectoryAreRejected(string steps)
        {
            Source(steps);
            Remote("1.0.0", new byte[] { 1 });

            var error = Assert.Throws<InvalidOperationException>(() => Install());

            Assert.Contains("outside", error.Message);
            AssertNoWorkDirectories();
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
            definitions.Discover(root);
            settings.UpdateSettings("app", s => s.Version = "1.0.0");
            Remote("2.0.0", new byte[] { 1 });

            Update();

            Assert.Equal("1.0.0", settings.GetSettings("app").Version);
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
    }
}
