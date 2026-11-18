using System;

namespace DevTools.Sources;

// An index in data/indexes.json with the time it was downloaded.
public class SyncedIndex
{
    public DateTimeOffset Synced { get; set; }
    public RepositoryIndex Index { get; set; }
}
