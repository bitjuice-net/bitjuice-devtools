using System.Collections.Generic;
using System.Text.Json.Serialization;
using DevTools.Installation.Models;

namespace DevTools.Catalog.Models;

public class ToolManifest
{
    public const string FileName = ".app";

    public string Name { get; set; }

    // Bumped in a manifest repository whenever the manifest changes.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Revision { get; set; }

    public string Version { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Versioned { get; set; }

    public List<string> Paths { get; set; }
    public Dictionary<string, string> Envs { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ToolInstall Install { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ManifestOrigin Origin { get; set; }
}
