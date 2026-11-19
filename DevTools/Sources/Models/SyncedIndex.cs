using System;

namespace DevTools.Sources.Models;

// An index in cache/sources.json with the time it was downloaded.
public class SyncedIndex
{
    public DateTimeOffset Synced { get; set; }
    public RepositoryIndex Index { get; set; }
}
