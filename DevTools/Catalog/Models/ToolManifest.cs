using System.Collections.Generic;
using System.Text.Json.Serialization;
using DevTools.Installation.Models;

namespace DevTools.Catalog.Models;

public class ToolManifest
{
    public const string FileName = ".app";

    public string Name { get; set; }

    public string Version { get; set; }

    // The application lies right in its directory. Without it every subdirectory named like a version is a version.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Flat { get; set; }

    public List<string> Paths { get; set; }
    public Dictionary<string, string> Envs { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ToolInstall Install { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ManifestOrigin Origin { get; set; }
}
