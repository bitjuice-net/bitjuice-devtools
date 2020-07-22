using System.Collections.Generic;

namespace DevTools.App
{
    public interface IToolDefinitionCache
    {
        Dictionary<string, List<ToolDefinition>> Load();
        void Save(Dictionary<string, List<ToolDefinition>> apps);
    }
}