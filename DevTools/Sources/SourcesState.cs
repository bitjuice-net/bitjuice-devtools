using System.Collections.Generic;

namespace DevTools.Sources;

// data/sources.json: the registered repositories and what was fetched from them.
public class SourcesState
{
    public const string DefaultRepository = "bitjuice-net/bitjuice-devtools-apps";

    public List<string> Repositories { get; set; } = new() { DefaultRepository };
    public Dictionary<string, FetchedApp> Apps { get; set; } = new();
}
