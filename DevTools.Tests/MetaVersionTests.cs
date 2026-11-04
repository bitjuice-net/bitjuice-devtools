using System.Collections.Generic;
using System.Linq;
using DevTools.App;
using Microsoft.Extensions.Options;
using Xunit;

namespace DevTools.Tests
{
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
            manager = new ToolManager(Options.Create(new Settings()), definitions, settings);
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

        [Fact]
        public void GetSettings_DefaultsToLatest()
        {
            Assert.Equal("latest", settings.GetSettings("node").Version);
        }

        [Fact]
        public void GetPath_FollowsNewVersionWhenCacheChanges()
        {
            TestTools.CaptureConsole(() => manager.Select("node", "latest"));
            Assert.Contains(@"C:\Tools\node\24.11.1", TestTools.CaptureConsole(manager.GetPath));

            storage.Load<Dictionary<string, List<ToolDefinition>>>(TestTools.CacheFile)["node"].Add(TestTools.Definition("node", "25.0.0"));

            Assert.Contains(@"C:\Tools\node\25.0.0", TestTools.CaptureConsole(manager.GetPath));
        }

        [Fact]
        public void List_ShowsResolvedVersionAndEnabledStatus()
        {
            TestTools.CaptureConsole(() => manager.Select("node", "oldest"));

            var row = TestTools.CaptureConsole(() => manager.List(false)).Split('\n').Single(i => i.StartsWith("| node"));

            Assert.Contains("oldest (8.9.3)", row);
            Assert.Contains("Enabled", row);
        }

        [Fact]
        public void List_MarksMetaVersionWithoutAnyInstalledVersionAsOrphaned()
        {
            settings.UpdateSettings("ghost", s => s.Version = "latest");

            var row = TestTools.CaptureConsole(() => manager.List(true)).Split('\n').Single(i => i.StartsWith("| ghost"));

            Assert.Contains("Orphaned", row);
        }
    }
}
