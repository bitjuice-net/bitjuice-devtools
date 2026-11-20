using System.Text.Json.Serialization;

namespace DevTools.Catalog.Models;

// An entry of config/apps.json. With a source it is an application from a repository, `app restore` installs it again.
public class ToolSettings
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Source { get; set; }

    public string Version { get; set; }
    public bool IsDisabled { get; set; }
}