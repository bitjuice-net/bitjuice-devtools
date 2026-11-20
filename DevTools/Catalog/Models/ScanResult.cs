using System.Collections.Generic;

namespace DevTools.Catalog.Models;

// cache/apps.json: what the last scan found in the tools directory. Every directory in it is an application,
// one with .app is managed by devtools, one without it is only listed.
public class ScanResult
{
    // Null in a cache written by an earlier version, which is then scanned again.
    public SortedDictionary<string, ScannedApp> Apps { get; set; }

    // Directories without .app.
    public List<string> Unmanaged { get; set; } = new();
}
