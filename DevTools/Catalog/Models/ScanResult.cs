using System.Collections.Generic;

namespace DevTools.Catalog.Models;

// What a scan found in the tools directory.
public class ScanResult
{
    public SortedDictionary<string, ScannedApp> Apps { get; set; } = new();

    // Directories without .app.
    public List<string> Unmanaged { get; set; } = new();
}
