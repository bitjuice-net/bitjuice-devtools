using System.Collections.Generic;

namespace DevTools.Sources.Models;

// index.json in the root of a manifest repository: { "apps": ["age", "java"] }.
public class RepositoryIndex
{
    public const string FileName = "index.json";

    public List<string> Apps { get; set; } = new();
}
