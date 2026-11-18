using System.Collections.Generic;

namespace DevTools.Sources;

public class FetchedApp
{
    public string Repository { get; set; }
    public int Revision { get; set; }

    // SHA-256 of the files as they were fetched, a different local file was changed by hand.
    public Dictionary<string, string> Hashes { get; set; } = new();
}
