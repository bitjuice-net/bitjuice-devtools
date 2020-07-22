using System.Collections.Generic;

namespace DevTools.New
{
    public interface IToolDefinitionProvider
    {
        List<string> GetNames();
        List<ToolDefinition> GetVersions(string name);
        ToolDefinition GetVersion(string name, string version);
        void Discover(string root);
    }
}