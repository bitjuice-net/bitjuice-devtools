using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DevTools.App
{
    public class ToolManifest
    {
        public string Name { get; set; }
        public string Version { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Versioned { get; set; }

        public List<string> Paths { get; set; }
        public Dictionary<string, string> Envs { get; set; }
    }
}
