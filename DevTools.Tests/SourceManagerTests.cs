using System;
using System.Collections.Generic;
using System.IO;
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

    private static void Manifest(string repository, string name, int revision, string extra = "", int format = 1)
    {
        var directory = Directory.CreateDirectory(Path.Combine(repository, name)).FullName;
        File.WriteAllText(Path.Combine(directory, ".app"), $$"""{ "name": "{{name}}", "versioned": true, "paths": ["%APPDIR%"]{{extra}} }""");
        File.WriteAllText(Path.Combine(directory, ".source"), $$"""{ "format": {{format}}, "revision": {{revision}}, "versions": { "type": "github-releases", "repo": "a/b" }, "steps": [ { "type": "download", "asset": "*" } ] }""");
    }

    private void Index(string repository)
    {
        TestTools.CaptureConsole(() => sources.Index(repository));
        File.WriteAllText(Path.Combine(repository, RepositoryIndex.FileName), System.Text.Json.JsonSerializer.Serialize(storage.Load<RepositoryIndex>(RepositoryIndex.FileName),
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }));
    }

    private string Local(string name, string file)
    {
        return Path.Combine(root, name, file);
    }

    private FetchedApp Fetched(string name)
    {
        return storage.Load<SourcesState>("sources.json").Apps.GetValueOrDefault(name);
    }

    // A new instance, like the next devtools command, so the index is read again.
    private string Update(string application = null, bool force = false)
    {
        var next = new SourceManager(Options.Create(new Settings { RootPath = root }), storage, new ToolDefinitionProvider(storage), client);
        return TestTools.CaptureConsole(() => next.Update(application, force));
    }

    [Fact]
    public void Index_ListsRevisionsOfApplications()
    {
        Manifest(repo, "age", 3);
        Manifest(repo, "java", 1);
        Directory.CreateDirectory(Path.Combine(repo, ".github"));

        TestTools.CaptureConsole(() => sources.Index(repo));

        var index = storage.Load<RepositoryIndex>(RepositoryIndex.FileName);
        Assert.Equal(new[] { "age", "java" }, index.Apps.Keys);
        Assert.Equal(3, index.Apps["age"].Revision);
    }

    [Theory]
    [InlineData("""{ "name": "other", "versioned": true }""", """{ "revision": 1 }""", "is named other")]
    [InlineData("""{ "name": "age" }""", """{ "revision": 1 }""", "versioned")]
    [InlineData("""{ "name": "age", "versioned": true }""", """{ }""", "revision of 1 or more")]
    [InlineData("""{ "name": "age", "versioned": true }""", """{ "format": 2, "revision": 1 }""", "has format 2")]
    public void Index_RejectsInvalidManifests(string app, string source, string message)
    {
        var directory = Directory.CreateDirectory(Path.Combine(repo, "age")).FullName;
        File.WriteAllText(Path.Combine(directory, ".app"), app);
        File.WriteAllText(Path.Combine(directory, ".source"), source);

        var error = Assert.Throws<InvalidOperationException>(() => sources.Index(repo));

        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void Fetch_WritesManifestsOfTheFirstSourceThatHasTheApplication()
    {
        Manifest(other, "java", 1);
        Index(other);
        Manifest(repo, "age", 2);
        Index(repo);
        storage.Save("sources.json", new SourcesState { Repositories = new List<string> { other, repo } });

        TestTools.CaptureConsole(() => sources.Fetch("age"));

        Assert.Equal(File.ReadAllText(Path.Combine(repo, "age", ".source")), File.ReadAllText(Local("age", ".source")));
        Assert.True(File.Exists(Local("age", ".app")));
        Assert.Equal(repo, Fetched("age").Repository);
        Assert.Equal(2, Fetched("age").Revision);
    }

    [Fact]
    public void Fetch_FailsWhenNoSourceHasTheApplication()
    {
        Index(repo);

        var error = Assert.Throws<InvalidOperationException>(() => sources.Fetch("age"));

        Assert.Contains("No `.source` file found for age", error.Message);
    }

    [Fact]
    public void Fetch_RejectsNewerFormat()
    {
        Manifest(repo, "age", 1);
        Index(repo);
        Manifest(repo, "age", 1, format: 2);

        var error = Assert.Throws<InvalidOperationException>(() => sources.Fetch("age"));

        Assert.Contains("update devtools", error.Message);
        Assert.False(File.Exists(Local("age", ".source")));
    }

    [Fact]
    public void Update_GetsNewerRevision()
    {
        Manifest(repo, "age", 1);
        Index(repo);
        TestTools.CaptureConsole(() => sources.Fetch("age"));
        Manifest(repo, "age", 2, """, "envs": { "AGE": "1" }""");
        Index(repo);

        Update();

        Assert.Contains("AGE", File.ReadAllText(Local("age", ".app")));
        Assert.Equal(2, Fetched("age").Revision);
    }

    [Fact]
    public void Update_SkipsApplicationsAtTheSameRevision()
    {
        Manifest(repo, "age", 1);
        Index(repo);
        TestTools.CaptureConsole(() => sources.Fetch("age"));
        client.Paths.Clear();

        var output = Update();

        Assert.Contains("up to date", output);
        Assert.Equal(new[] { RepositoryIndex.FileName }, client.Paths);
    }

    [Fact]
    public void Update_KeepsFilesChangedByHandUnlessForced()
    {
        Manifest(repo, "age", 1);
        Index(repo);
        TestTools.CaptureConsole(() => sources.Fetch("age"));
        File.WriteAllText(Local("age", ".source"), "{ \"mine\": true }");
        Manifest(repo, "age", 2, """, "envs": { "AGE": "1" }""");
        Index(repo);

        var output = Update();

        Assert.Contains("has local changes", output);
        Assert.Contains("stays at revision 1", output);
        Assert.Contains("mine", File.ReadAllText(Local("age", ".source")));
        Assert.Contains("AGE", File.ReadAllText(Local("age", ".app")));
        Assert.Equal(1, Fetched("age").Revision);

        Update("age", force: true);

        Assert.DoesNotContain("mine", File.ReadAllText(Local("age", ".source")));
        Assert.Equal(2, Fetched("age").Revision);
    }

    [Fact]
    public void Update_OfLocalApplicationKeepsItsOwnFiles()
    {
        Directory.CreateDirectory(Path.Combine(root, "age"));
        File.WriteAllText(Local("age", ".app"), """{ "name": "age", "versioned": true }""");
        File.WriteAllText(Local("age", ".source"), "{ \"mine\": true }");
        Manifest(repo, "age", 1);
        Index(repo);

        var output = Update("age");

        Assert.Contains("has local changes", output);
        Assert.Contains("mine", File.ReadAllText(Local("age", ".source")));
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
}
