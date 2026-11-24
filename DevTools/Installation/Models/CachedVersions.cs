using System;
using System.Collections.Generic;

namespace DevTools.Installation.Models;

// cache/versions/<application>.json
public class CachedVersions
{
    // The version source the versions come from, VersionCache.Key.
    public string Key { get; set; }

    public DateTimeOffset Time { get; set; }
    public List<RemoteVersion> Versions { get; set; }
}
