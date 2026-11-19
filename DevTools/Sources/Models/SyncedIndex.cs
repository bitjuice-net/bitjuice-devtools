using System;

namespace DevTools.Sources.Models;

// An index in cache/indexes.json with the time it was downloaded.
public class SyncedIndex
{
    public DateTimeOffset Synced { get; set; }
    public RepositoryIndex Index { get; set; }
}
