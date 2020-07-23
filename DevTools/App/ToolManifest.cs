using System.Collections.Generic;

namespace DevTools.App
{
    public class ToolManifest
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public List<string> Paths { get; set; }
    }
}
