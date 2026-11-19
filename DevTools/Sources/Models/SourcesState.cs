using System.Collections.Generic;

namespace DevTools.Sources.Models;

// data/sources.json: the registered manifest repositories. Where an application came from is in its .app.
public class SourcesState
{
    public const string DefaultRepository = "bitjuice-net/bitjuice-devtools-apps";

    public List<string> Repositories { get; set; } = new() { DefaultRepository };
}
