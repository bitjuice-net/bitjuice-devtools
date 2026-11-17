using System.Collections.Generic;

namespace DevTools.Sources;

// index.json in the root of a manifest repository, written by `source index`.
public class RepositoryIndex
{
    public const string FileName = "index.json";

    public int Format { get; set; } = 1;
    public SortedDictionary<string, IndexEntry> Apps { get; set; } = new();
}

public class IndexEntry
{
    public int Revision { get; set; }
}

// data/sources.json: the registered repositories and what was fetched from them.
public class SourcesState
{
    public const string DefaultRepository = "bitjuice-net/bitjuice-devtools-apps";

    public List<string> Repositories { get; set; } = new() { DefaultRepository };
    public Dictionary<string, FetchedApp> Apps { get; set; } = new();
}

public class FetchedApp
{
    public string Repository { get; set; }
    public int Revision { get; set; }

    // SHA-256 of the files as they were fetched, a different local file was changed by hand.
    public Dictionary<string, string> Hashes { get; set; } = new();
}
