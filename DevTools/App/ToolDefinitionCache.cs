using System.Collections.Generic;

namespace DevTools.App
{
    public class ToolDefinitionCache : IToolDefinitionCache
    {
        private const string FileName = "cache.json";

        public Dictionary<string, List<ToolDefinition>> Load()
        {
            return JsonEx.DeserializeFromFile<Dictionary<string, List<ToolDefinition>>>(FileName);
        }

        public void Save(Dictionary<string, List<ToolDefinition>> apps)
        {
            JsonEx.SerializeToFile(FileName, apps);
        }
    }
}