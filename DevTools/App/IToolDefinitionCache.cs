using System.Collections.Generic;

namespace DevTools.New
{
    public interface IToolDefinitionCache
    {
        Dictionary<string, List<ToolDefinition>> Load();
        void Save(Dictionary<string, List<ToolDefinition>> apps);
    }
}