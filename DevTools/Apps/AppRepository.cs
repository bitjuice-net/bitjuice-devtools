using System.Collections.Generic;

namespace DevTools.Apps
{
    public class AppRepository
    {
        public string Path { get; set; }
        public Dictionary<string, AppDeclaration> Apps { get; set; }
    }
}