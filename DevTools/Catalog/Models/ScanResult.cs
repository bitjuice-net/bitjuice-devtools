using System.Collections.Generic;

namespace DevTools.Catalog.Models;

// cache/apps.json: what the last scan found in the tools directory. Every directory in it is an application,
// one with .app is managed by devtools, one without it is only listed.
public class ScanResult
{
    public SortedDictionary<string, ScannedApp> Apps { get; set; } = new();

    // Directories without .app.
    public List<string> Unmanaged { get; set; } = new();
}
