using System.Collections.Generic;

namespace DevTools.Catalog;

public interface IToolDefinitionProvider
{
    List<string> GetNames();
    List<ToolDefinition> GetVersions(string application);
    ToolDefinition GetVersion(string application, string version);
    void Discover(string root);
    string FindSourceDirectory(string root, string application);
}