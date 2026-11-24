using System.Collections.Generic;
using System.IO;
using DevTools.Installation;
using DevTools.Installation.Models;
using Xunit;

namespace DevTools.Tests;

public class VersionCacheTests
{
    private readonly MemoryStorage storage = new();
    private readonly FakeTime time = new();

    private static List<RemoteVersion> Versions(string version)
    {
        return new List<RemoteVersion> { new(version, "v" + version, new List<RemoteAsset>()) };
    }

    [Fact]
    public void EveryApplicationHasItsOwnFile()
    {
        var cache = new VersionCache(storage, time);

        cache.Set("go", "source-go", Versions("1.0.0"));
        cache.Set("node", "source-node", Versions("2.0.0"));

        Assert.EndsWith(Path.Combine("versions", "go.json"), VersionCache.FileName("go"));
        Assert.True(storage.Exists(VersionCache.FileName("go")));
        Assert.True(storage.Exists(VersionCache.FileName("node")));
        Assert.Equal("1.0.0", Assert.Single(cache.Get("go", "source-go")).Version);
        Assert.Equal("2.0.0", Assert.Single(cache.Get("node", "source-node")).Version);
    }

    [Fact]
    public void AnotherSourceOfTheApplicationIsNotCached()
    {
        var cache = new VersionCache(storage, time);
        cache.Set("go", "old-source", Versions("1.0.0"));

        Assert.Null(cache.Get("go", "new-source"));
    }

    [Fact]
    public void VersionsAreKeptForAnHour()
    {
        var cache = new VersionCache(storage, time);
        cache.Set("go", "source", Versions("1.0.0"));

        time.Now += VersionCache.Duration;

        Assert.Null(cache.Get("go", "source"));
        Assert.Null(cache.Get("missing", "source"));
    }
}
