using System;
using System.Collections.Generic;
using System.IO;
using DevTools.Infrastructure;
using DevTools.Installation.Models;

namespace DevTools.Installation;

// Versions found by a version source, kept for an hour in cache/versions/<application>.json.
// Updating every application would otherwise ask GitHub again each time and soon hit its rate limit.
// One file per application, so saving the versions of one does not rewrite the others.
public class VersionCache(IStorage storage, TimeProvider time = null)
{
    public static readonly TimeSpan Duration = TimeSpan.FromHours(1);

    private readonly TimeProvider time = time ?? TimeProvider.System;

    public static string FileName(string application)
    {
        return PathUtils.GetCachePath(Path.Combine("versions", application + ".json"));
    }

    public static string Key(VersionSource source)
    {
        return new[] { source.Type, source.Repo, source.Tag, source.Url, source.Pattern }.Join("|");
    }

    // The cached versions, null when there are none, they are older than an hour or .app now has another source.
    public List<RemoteVersion> Get(string application, string key)
    {
        var entry = storage.Load<CachedVersions>(FileName(application));
        return entry.Key == key && !IsExpired(entry) ? entry.Versions : null;
    }

    public void Set(string application, string key, List<RemoteVersion> versions)
    {
        storage.Save(FileName(application), new CachedVersions { Key = key, Time = time.GetUtcNow(), Versions = versions });
    }

    private bool IsExpired(CachedVersions entry)
    {
        return entry.Versions == null || time.GetUtcNow() - entry.Time >= Duration;
    }
}
