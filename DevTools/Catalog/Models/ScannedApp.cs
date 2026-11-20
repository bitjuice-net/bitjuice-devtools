using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DevTools.Catalog.Models;

public class ScannedApp
{
    // The repository in origin of .app, null for a manifest made by hand.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Source { get; set; }

    // Empty for an application without installed versions.
    public List<ToolDefinition> Versions { get; set; } = new();
}
