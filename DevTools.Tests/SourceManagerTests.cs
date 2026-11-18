using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using DevTools.Catalog;
using DevTools.Infrastructure;
using DevTools.Sources;
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
    private readonly SourceManager sources;

    public SourceManagerTests()
    {
        root = Directory.CreateDirectory(Path.Combine(temp, "tools")).FullName;
        repo = Directory.CreateDirectory(Path.Combine(temp, "repo")).FullName;
        other = Directory.CreateDirectory(Path.Combine(temp, "other")).FullName;
        sources = new SourceManager(Options.Create(new Settings { RootPath = root }), storage, new ToolDefinitionProvider(storage), client);
        storage.Save("sources.json", new SourcesState { Repositories = new List<string> { repo } });
    }

    public void Dispose()
    {
        Directory.Delete(temp, true);
    }

    private static void Manifest(string repository, string name, int revision, string extra = "")
    {
        var directory = Directory.CreateDirectory(Path.Combine(repository, name)).FullName;
        File.WriteAllText(Path.Combine(directory, ".app"), $$"""{ "name": "{{name}}", "versioned": true, "paths": ["%APPDIR%"]{{extra}} }""");
        File.WriteAllText(Path.Combine(directory, ".source"), $$"""{ "revision": {{revision}}, "versions": { "type": "github-releases", "repo": "a/b" }, "steps": [ { "type": "download", "asset": "*" } ] }""");
    }

    // index.json as the repository maintainer writes it, from the revisions in .source.
    private static void Index(string repository)
    {
        var apps = new Dictionary<string, IndexEntry>();
        foreach (var directory in Directory.GetDirectories(repository))
        {
            var source = Path.Combine(directory, ".source");
            if (File.Exists(source))
                apps[Path.GetFileName(directory)] = new IndexEntry { Revision = JsonStorage.Parse<DevTools.Installation.ToolSource>(File.ReadAllBytes(source)).Revision };
        }

        File.WriteAllText(Path.Combine(repository, RepositoryIndex.FileName), System.Text.Json.JsonSerializer.Serialize(new { apps = apps.ToDictionary(i => i.Key, i => new { revision = i.Value.Revision }) }));
    }

    private string Local(string name, string file)
    {
        return Path.Combine(root, name, file);
    }

    private FetchedApp Fetched(string name)
    {
        return storage.Load<SourcesState>("sources.json").Apps.GetValueOrDefault(name);
    }

    // A new instance, like the next devtools command, that reads the synced indexes again.
    private readonly FakeTime time = new();

    private SourceManager Next()
    {
        return new SourceManager(Options.Create(new Settings { RootPath = root }), storage, new ToolDefinitionProvider(storage), client, time);
    }

    private string Refresh(string application)
    {
        return TestTools.CaptureConsole(() => Next().Refresh(application));
    }

    private string Sync()
    {
        return TestTools.CaptureConsole(() => Next().Sync());
    }

    [Theory]
    [InlineData("""{ "name": "other", "versioned": true }""", """{ "revision": 1 }""", "is named other")]
    [InlineData("""{ "name": "age" }""", """{ "revision": 1 }""", "versioned")]
    [InlineData("""{ "name": "age", "versioned": true }""", """{ }""", "revision of 1 or more")]
    public void Refresh_RejectsInvalidManifests(string app, string source, string message)
    {
        var directory = Directory.CreateDirectory(Path.Combine(repo, "age")).FullName;
        File.WriteAllText(Path.Combine(directory, ".app"), app);
        File.WriteAllText(Path.Combine(directory, ".source"), source);
        File.WriteAllText(Path.Combine(repo, RepositoryIndex.FileName), """{ "apps": { "age": { "revision": 1 } } }""");

        var error = Assert.Throws<InvalidOperationException>(() => Next().Refresh("age"));

        Assert.Contains(message, error.Message);
        Assert.False(File.Exists(Local("age", ".source")));
    }

    [Fact]
    public void Refresh_WritesManifestsOfTheFirstSourceThatHasTheApplication()
    {
        Manifest(other, "java", 1);
        Index(other);
        Manifest(repo, "age", 2);
        Index(repo);
        storage.Save("sources.json", new SourcesState { Repositories = new List<string> { other, repo } });

        Refresh("age");

        Assert.Equal(File.ReadAllText(Path.Combine(repo, "age", ".source")), File.ReadAllText(Local("age", ".source")));
        Assert.True(File.Exists(Local("age", ".app")));
        Assert.Equal(repo, Fetched("age").Repository);
        Assert.Equal(2, Fetched("age").Revision);
    }

    [Theory]
    [InlineData(1, 2, "other")]
    [InlineData(2, 1, "repo")]
    [InlineData(1, 1, "repo")]
    public void Refresh_TakesTheNewestRevisionOfAllSources(int first, int second, string expected)
    {
        Manifest(repo, "age", first);
        Index(repo);
        Manifest(other, "age", second);
        Index(other);
        storage.Save("sources.json", new SourcesState { Repositories = new List<string> { repo, other } });

        Refresh("age");

        Assert.Equal(expected == "repo" ? repo : other, Fetched("age").Repository);
    }

    [Fact]
    public void Refresh_StaysWithTheSourceTheApplicationCameFrom()
    {
        Manifest(repo, "age", 1);
        Index(repo);
        Refresh("age");
        Manifest(other, "age", 5);
        Index(other);
        storage.Load<SourcesState>("sources.json").Repositories.Add(other);

        Refresh("age");

        Assert.Equal(repo, Fetched("age").Repository);
        Assert.Equal(1, Fetched("age").Revision);
    }

    [Fact]
    public void Refresh_FailsWhenNoSourceHasTheApplication()
    {
        Index(repo);

        var error = Assert.Throws<InvalidOperationException>(() => Next().Refresh("age"));

        Assert.Contains("No `.source` file found for age", error.Message);
        Assert.Contains("source sync", error.Message);
    }

    [Fact]
    public void Refresh_GetsNewerRevisionAfterSync()
    {
        Manifest(repo, "age", 1);
        Index(repo);
        Refresh("age");
        Manifest(repo, "age", 2, """, "envs": { "AGE": "1" }""");
        Index(repo);

        Refresh("age");
        Assert.DoesNotContain("AGE", File.ReadAllText(Local("age", ".app")));

        Sync();
        Refresh("age");

        Assert.Contains("AGE", File.ReadAllText(Local("age", ".app")));
        Assert.Equal(2, Fetched("age").Revision);
    }

    [Fact]
    public void Refresh_AtTheSyncedRevisionDownloadsNothing()
    {
        Manifest(repo, "age", 1);
        Index(repo);
        Refresh("age");
        client.Paths.Clear();

        Refresh("age");

        Assert.Empty(client.Paths);
    }

    [Fact]
    public void Refresh_KeepsFilesChangedByHandUntilTheyAreDeleted()
    {
        Manifest(repo, "age", 1);
        Index(repo);
        Refresh("age");
        File.WriteAllText(Local("age", ".source"), "{ \"mine\": true }");
        Manifest(repo, "age", 2, """, "envs": { "AGE": "1" }""");
        Index(repo);
        Sync();

        var output = Refresh("age");

        Assert.Contains("has local changes, delete it", output);
        Assert.Contains("mine", File.ReadAllText(Local("age", ".source")));
        Assert.Contains("AGE", File.ReadAllText(Local("age", ".app")));
        Assert.Equal(1, Fetched("age").Revision);

        File.Delete(Local("age", ".source"));
        Refresh("age");

        Assert.DoesNotContain("mine", File.ReadAllText(Local("age", ".source")));
        Assert.Equal(2, Fetched("age").Revision);
    }

    // Local manifests not fetched from a source count as their own revision, 1 when they have none.
    [Theory]
    [InlineData("", 1, false)]
    [InlineData("", 2, true)]
    [InlineData(""", "revision": 3""", 2, false)]
    [InlineData(""", "revision": 3""", 4, true)]
    public void Refresh_ReplacesLocalManifestsWithANewerRevision(string localRevision, int sourceRevision, bool replaced)
    {
        Directory.CreateDirectory(Path.Combine(root, "age"));
        File.WriteAllText(Local("age", ".app"), """{ "name": "age", "versioned": true }""");
        File.WriteAllText(Local("age", ".source"), $$"""{ "mine": true{{localRevision}} }""");
        Manifest(repo, "age", sourceRevision);
        Index(repo);

        var changed = false;
        TestTools.CaptureConsole(() => changed = Next().Refresh("age"));

        Assert.Equal(replaced, changed);
        Assert.Equal(!replaced, File.ReadAllText(Local("age", ".source")).Contains("mine"));
        Assert.Equal(replaced ? sourceRevision : null, Fetched("age")?.Revision);
    }

    [Fact]
    public void Sync_KeepsThePreviousListOfAnUnavailableSource()
    {
        Manifest(repo, "age", 1);
        Index(repo);
        Sync();
        File.Delete(Path.Combine(repo, RepositoryIndex.FileName));

        var error = TestTools.CaptureError(() => Sync());
        Refresh("age");

        Assert.Contains("kept the previous list", error);
        Assert.True(File.Exists(Local("age", ".source")));
    }

    [Fact]
    public void Add_RequiresIndexAndRemembersRepository()
    {
        var error = Assert.Throws<InvalidOperationException>(() => sources.Add(other));
        Assert.Contains("has no index.json", error.Message);

        Index(other);
        TestTools.CaptureConsole(() => sources.Add(other));
        TestTools.CaptureConsole(() => sources.Remove(repo));

        Assert.Equal(new[] { other }, storage.Load<SourcesState>("sources.json").Repositories);
    }

    [Theory]
    [InlineData("not a repo")]
    [InlineData("owner/repo/extra")]
    public void Add_RejectsInvalidNames(string repository)
    {
        Assert.Throws<InvalidOperationException>(() => sources.Add(repository));
    }

    [Fact]
    public void NewState_UsesDefaultRepository()
    {
        Assert.Equal(new[] { SourcesState.DefaultRepository }, new SourcesState().Repositories);
    }

    [Fact]
    public void List_ShowsCountsAndTheApplicationsOfOneRepository()
    {
        Manifest(repo, "age", 2);
        Manifest(repo, "java", 1);
        Index(repo);
        Refresh("age");

        var all = TestTools.CaptureConsole(() => Next().List(null));
        var one = TestTools.CaptureConsole(() => Next().List(repo));

        Assert.Matches(@"(?m)^.*repo\s+2\s*$", all);
        Assert.Matches(@"(?m)^age\s+2\s+2\s*$", one);
        Assert.Matches(@"(?m)^java\s+1\s*$", one);
    }

    [Fact]
    public void JsonStorage_SaveWritesANewState()
    {
        var storage = new JsonStorage();
        storage.Save(Path.Combine(temp, "data", "tools.json"), new Dictionary<string, string>());
        var first = JsonStorage.ReadState(Path.Combine(temp, "data"));
        System.Threading.Thread.Sleep(1);
        storage.Save(Path.Combine(temp, "data", "tools.json"), new Dictionary<string, string>());

        Assert.NotEqual("", first);
        Assert.NotEqual(first, JsonStorage.ReadState(Path.Combine(temp, "data")));
    }

    [Fact]
    public void Refresh_DownloadsAListOlderThanADay()
    {
        Manifest(repo, "age", 1);
        Index(repo);
        Refresh("age");
        Manifest(repo, "age", 2, """, "envs": { "AGE": "1" }""");
        Index(repo);

        Refresh("age");
        Assert.Equal(1, Fetched("age").Revision);

        time.Now += SourceManager.MaxIndexAge;
        Refresh("age");
        Assert.Equal(2, Fetched("age").Revision);
    }

    [Fact]
    public void Refresh_UsesAnOldListWhenTheSourceIsUnavailable()
    {
        Manifest(repo, "age", 1);
        Index(repo);
        Sync();
        File.Delete(Path.Combine(repo, RepositoryIndex.FileName));
        time.Now += SourceManager.MaxIndexAge;

        var error = TestTools.CaptureError(() => Refresh("age"));

        Assert.Contains("using the list from", error);
        Assert.True(File.Exists(Local("age", ".source")));
    }
}
