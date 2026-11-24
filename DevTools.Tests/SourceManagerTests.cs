using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using DevTools.Catalog;
using DevTools.Catalog.Models;
using DevTools.Infrastructure;
using DevTools.Sources;
using DevTools.Sources.Models;
using Microsoft.Extensions.Options;
using Xunit;

namespace DevTools.Tests;

public sealed class SourceManagerTests : IDisposable
{
    // Reads local repositories like the real client and remembers what was read.
    private class RecordingClient : IRepositoryClient
    {
        private readonly RepositoryClient client = new(new HttpClient());
        public List<string> Paths { get; } = new();

        public Task<byte[]> GetFileAsync(string repository, string path)
        {
            Paths.Add(path);
            return client.GetFileAsync(repository, path);
        }
    }

    private readonly string temp = Directory.CreateTempSubdirectory("devtools-sources-").FullName;
    private readonly string root;
    private readonly string repo;
    private readonly string other;
    private readonly MemoryStorage storage = new();
    private readonly RecordingClient client = new();

    public SourceManagerTests()
    {
        root = Directory.CreateDirectory(Path.Combine(temp, "tools")).FullName;
        repo = Directory.CreateDirectory(Path.Combine(temp, "repo")).FullName;
        other = Directory.CreateDirectory(Path.Combine(temp, "other")).FullName;
        Sources(repo);
    }

    public void Dispose()
    {
        Directory.Delete(temp, true);
    }

    private void Sources(params string[] repositories)
    {
        storage.Save("config/sources.json", new SourcesState { Repositories = repositories.ToList() });
    }

    private static void Manifest(string repository, string name, string extra = "")
    {
        File.WriteAllText(RepositoryFile(repository, name), $$"""
            { "name": "{{name}}", "paths": ["%APPDIR%"]{{extra}},
              "install": { "versions": { "type": "github-releases", "repo": "a/b" }, "steps": [ { "type": "download", "asset": "*" } ] } }
            """);
        Index(repository);
    }

    // index.json as the repository has it: the names of its manifests.
    private static void Index(string repository)
    {
        var apps = Directory.CreateDirectory(Path.Combine(repository, "apps")).GetFiles("*.app").Select(i => Path.GetFileNameWithoutExtension(i.Name)).ToList();
        File.WriteAllText(Path.Combine(repository, RepositoryIndex.FileName), System.Text.Json.JsonSerializer.Serialize(new { apps }));
    }

    private static string RepositoryFile(string repository, string name)
    {
        Directory.CreateDirectory(Path.Combine(repository, "apps"));
        return Path.Combine(repository, SourceManager.ManifestPath(name));
    }

    private string Local(string name)
    {
        return Path.Combine(root, name, ToolManifest.FileName);
    }

    private void HandMade(string content)
    {
        Directory.CreateDirectory(Path.Combine(root, "age"));
        File.WriteAllText(Local("age"), content);
    }

    // The local .app when it was fetched from a source, null otherwise.
    private ToolManifest Fetched(string name)
    {
        var fileName = Local(name);
        var manifest = File.Exists(fileName) ? new JsonStorage().Load<ToolManifest>(fileName) : null;
        return manifest?.Origin != null ? manifest : null;
    }

    // A new instance, like the next devtools command.
    private SourceManager Next()
    {
        return new SourceManager(Options.Create(new Settings { RootPath = root }), storage, new ToolDefinitionProvider(storage), client);
    }

    private string Refresh(string application, string source = null)
    {
        return TestTools.CaptureConsole(() => Next().Refresh(application, source));
    }

    [Theory]
    [InlineData("""{ "name": "other", "install": { } }""", "is named other")]
    [InlineData("""{ "name": "age", "flat": true, "install": { } }""", "is flat")]
    [InlineData("""{ "name": "age" }""", "no install section")]
    public void Refresh_RejectsInvalidManifests(string app, string message)
    {
        File.WriteAllText(RepositoryFile(repo, "age"), app);
        Index(repo);

        var error = Assert.Throws<InvalidOperationException>(() => Next().Refresh("age"));

        Assert.Contains(message, error.Message);
        Assert.False(File.Exists(Local("age")));
    }

    [Fact]
    public void Refresh_WritesTheManifestOfTheSourceThatHasTheApplication()
    {
        Manifest(other, "java");
        Manifest(repo, "age");
        Sources(other, repo);

        Refresh("age");

        Assert.Equal(repo, Fetched("age").Origin.Repository);
        Assert.NotNull(Fetched("age").Install);
        Assert.False(ManifestHash.IsModified(Local("age")));
    }

    [Fact]
    public void Refresh_AsksWhichSourceWhenMoreThanOneHasTheApplication()
    {
        Manifest(repo, "age");
        Manifest(other, "age");
        Sources(repo, other);

        var error = Assert.Throws<InvalidOperationException>(() => Next().Refresh("age"));
        Assert.Contains("more than one source", error.Message);
        Assert.Contains("--source", error.Message);
        Assert.False(File.Exists(Local("age")));

        Refresh("age", other);
        Assert.Equal(other, Fetched("age").Origin.Repository);
    }

    [Fact]
    public void Refresh_StaysWithTheSourceTheApplicationCameFrom()
    {
        Manifest(repo, "age");
        Refresh("age");
        Manifest(other, "age", """, "envs": { "AGE": "1" }""");
        Sources(repo, other);

        Refresh("age");

        Assert.Equal(repo, Fetched("age").Origin.Repository);
        Assert.DoesNotContain("AGE", File.ReadAllText(Local("age")));
    }

    [Fact]
    public void Refresh_FailsWhenNoSourceHasTheApplication()
    {
        Index(repo);

        var error = Assert.Throws<InvalidOperationException>(() => Next().Refresh("age"));

        Assert.Contains("No source has age", error.Message);
    }

    [Fact]
    public void Refresh_GetsAChangedManifestRightAway()
    {
        Manifest(repo, "age");
        Refresh("age");
        Manifest(repo, "age", """, "envs": { "AGE": "1" }""");

        var changed = false;
        TestTools.CaptureConsole(() => changed = Next().Refresh("age"));

        Assert.True(changed);
        Assert.Contains("AGE", File.ReadAllText(Local("age")));
        Assert.False(ManifestHash.IsModified(Local("age")));
    }

    [Fact]
    public void Refresh_OfAnUnchangedManifestChangesNothing()
    {
        Manifest(repo, "age");
        Refresh("age");
        var written = File.GetLastWriteTimeUtc(Local("age"));

        var changed = true;
        TestTools.CaptureConsole(() => changed = Next().Refresh("age"));

        Assert.False(changed);
        Assert.Equal(written, File.GetLastWriteTimeUtc(Local("age")));
    }

    [Fact]
    public void Refresh_ReadsTheIndexOncePerCommand()
    {
        Manifest(repo, "age");
        var sources = Next();

        TestTools.CaptureConsole(() => sources.Refresh("age"));
        TestTools.CaptureConsole(() => sources.Refresh("age"));

        Assert.Single(client.Paths, RepositoryIndex.FileName);
    }

    [Fact]
    public void Refresh_KeepsAManifestChangedByHandUntilItIsReset()
    {
        Manifest(repo, "age");
        Refresh("age");
        File.WriteAllText(Local("age"), File.ReadAllText(Local("age")).Replace("%APPDIR%", "%APPDIR%/mine"));

        Assert.DoesNotContain("local changes", Refresh("age"));

        Manifest(repo, "age", """, "envs": { "AGE": "1" }""");
        var output = Refresh("age");

        Assert.Contains("has local changes. `app reset age`", output);
        Assert.Contains("mine", File.ReadAllText(Local("age")));

        TestTools.CaptureConsole(() => Next().Reset("age"));

        Assert.DoesNotContain("mine", File.ReadAllText(Local("age")));
        Assert.Contains("AGE", File.ReadAllText(Local("age")));
    }

    [Fact]
    public void Refresh_KeepsTheManifestWhenItsSourceNoLongerHasIt()
    {
        Manifest(repo, "age");
        Refresh("age");
        File.Delete(RepositoryFile(repo, "age"));
        Index(repo);
        Manifest(other, "age");
        Sources(repo, other);

        var changed = true;
        var error = TestTools.CaptureError(() => TestTools.CaptureConsole(() => changed = Next().Refresh("age")));

        Assert.False(changed);
        Assert.Contains("no longer has age", error);
        Assert.Equal(repo, Fetched("age").Origin.Repository);

        TestTools.CaptureConsole(() => Next().Reset("age", other));
        Assert.Equal(other, Fetched("age").Origin.Repository);
    }

    [Fact]
    public void ChangedFormattingIsNotALocalChange()
    {
        Manifest(repo, "age");
        Refresh("age");

        var fileName = Local("age");
        File.WriteAllText(fileName, System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(fileName))!.ToJsonString());

        Assert.False(ManifestHash.IsModified(fileName));
    }

    [Fact]
    public void Reset_GetsAManifestThatIsMissing()
    {
        Manifest(repo, "age");

        TestTools.CaptureConsole(() => Next().Reset("age"));

        Assert.Equal(repo, Fetched("age").Origin.Repository);
    }

    [Fact]
    public void Refresh_LeavesAHandMadeManifestWithoutInstallAndExplains()
    {
        HandMade("""{ "name": "age" }""");
        Manifest(repo, "age");

        var error = Assert.Throws<InvalidOperationException>(() => Next().Refresh("age"));

        Assert.Contains("`app reset age`", error.Message);
        Assert.Null(Fetched("age"));
    }

    [Fact]
    public void Refresh_NeverReplacesAHandMadeManifestWithInstall()
    {
        HandMade("""{ "name": "age", "mine": true, "install": { } }""");
        Manifest(repo, "age");

        var changed = true;
        TestTools.CaptureConsole(() => changed = Next().Refresh("age"));

        Assert.False(changed);
        Assert.Contains("mine", File.ReadAllText(Local("age")));
        Assert.Empty(client.Paths);

        TestTools.CaptureConsole(() => Next().Reset("age"));
        Assert.Equal(repo, Fetched("age").Origin.Repository);
    }

    [Fact]
    public void Add_RequiresIndexAndRemembersRepository()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Next().Add(other));
        Assert.Contains("has no index.json", error.Message);

        Index(other);
        TestTools.CaptureConsole(() => Next().Add(other));
        TestTools.CaptureConsole(() => Next().Remove(repo));

        Assert.Equal(new[] { other }, storage.Load<SourcesState>("config/sources.json").Repositories);
    }

    [Theory]
    [InlineData("not a repo")]
    [InlineData("owner/repo/extra")]
    public void Add_RejectsInvalidNames(string repository)
    {
        Assert.Throws<InvalidOperationException>(() => Next().Add(repository));
    }

    [Fact]
    public void NewState_UsesDefaultRepository()
    {
        Assert.Equal(new[] { SourcesState.DefaultRepository }, new SourcesState().Repositories);
    }

    [Fact]
    public void List_ShowsCountsAndTheApplicationsOfOneRepository()
    {
        Manifest(repo, "age");
        Manifest(repo, "java");
        Refresh("age");

        var all = TestTools.CaptureConsole(() => Next().List(null));
        var one = TestTools.CaptureConsole(() => Next().List(repo));

        Assert.Matches(@"(?m)^.*repo\s+2\s*$", all);
        Assert.Matches(@"(?m)^age\s+fetched\s*$", one);
        Assert.Matches(@"(?m)^java\s*$", one);
    }

}
