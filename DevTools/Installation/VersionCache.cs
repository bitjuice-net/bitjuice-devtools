using System;
using System.Collections.Generic;
using System.Linq;
using DevTools.Infrastructure;

namespace DevTools.Installation;

// Versions found by a version source, kept for an hour in data/versions.json.
// Updating every application would otherwise ask GitHub again each time and soon hit its rate limit.
public class VersionCache(IStorage storage, TimeProvider time = null)
{
    public static readonly TimeSpan Duration = TimeSpan.FromHours(1);

    private static readonly string FileName = PathUtils.GetDataPath("versions.json");

    private readonly TimeProvider time = time ?? TimeProvider.System;

    private Dictionary<string, CachedVersions> entries;
    private Dictionary<string, CachedVersions> Entries => entries ??= storage.Load<Dictionary<string, CachedVersions>>(FileName);

    public static string Key(VersionSource source)
    {
        return new[] { source.Type, source.Repo, source.Tag, source.Url, source.Pattern }.Join("|");
    }

    // The cached versions, null when there are none or they are older than an hour.
    public List<RemoteVersion> Get(string key)
    {
        return Entries.TryGetValue(key, out var entry) && !IsExpired(entry) ? entry.Versions : null;
    }

    public void Set(string key, List<RemoteVersion> versions)
    {
        foreach (var expired in Entries.Where(i => IsExpired(i.Value)).Select(i => i.Key).ToList())
            Entries.Remove(expired);

        Entries[key] = new CachedVersions { Time = time.GetUtcNow(), Versions = versions };
        storage.Save(FileName, Entries, changesState: false);
    }

    private bool IsExpired(CachedVersions entry)
    {
        return entry.Versions == null || time.GetUtcNow() - entry.Time >= Duration;
    }
}

public class CachedVersions
{
    public DateTimeOffset Time { get; set; }
    public List<RemoteVersion> Versions { get; set; }
}
