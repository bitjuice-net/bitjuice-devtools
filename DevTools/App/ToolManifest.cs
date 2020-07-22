using System.Collections.Generic;

namespace DevTools.New
{
    public class ToolManifest
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public List<string> Paths { get; set; }
    }
}
