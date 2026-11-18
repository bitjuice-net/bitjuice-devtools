using System.Collections.Generic;

namespace DevTools.Catalog;

public interface IToolDefinitionProvider
{
    List<string> GetNames();
    List<ToolDefinition> GetVersions(string application);
    ToolDefinition GetVersion(string application, string version);
    void Scan(string root);
    bool IsUpToDate(string root);
    string FindSourceDirectory(string root, string application);
    string FindDirectory(string root, string application);
}