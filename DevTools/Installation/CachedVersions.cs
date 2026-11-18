using System;
using System.Collections.Generic;

namespace DevTools.Installation;

public class CachedVersions
{
    public DateTimeOffset Time { get; set; }
    public List<RemoteVersion> Versions { get; set; }
}
