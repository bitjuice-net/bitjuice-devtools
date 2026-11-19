using System.Collections.Generic;

namespace DevTools.Catalog.Models;

// cache/scan.json: what the last scan looked at and when it was last written.
public class ScanSnapshot
{
    public string Root { get; set; }

    // UTC ticks of the start of the scan.
    public long Scanned { get; set; }
    public Dictionary<string, long> Times { get; set; } = new();
}
