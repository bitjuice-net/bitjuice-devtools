using System.Collections.Generic;

namespace DevTools.Sources;

// index.json in the root of a manifest repository.
public class RepositoryIndex
{
    public const string FileName = "index.json";

    public SortedDictionary<string, IndexEntry> Apps { get; set; } = new();
}
