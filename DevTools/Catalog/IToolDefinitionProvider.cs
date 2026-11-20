using System.Collections.Generic;
using DevTools.Catalog.Models;

namespace DevTools.Catalog;

public interface IToolDefinitionProvider
{
    List<string> GetNames();
    List<ToolDefinition> GetVersions(string application);
    ToolDefinition GetVersion(string application, string version);
    void Scan(string root);
    bool IsScanned();
    string FindSourceDirectory(string root, string application);
    string FindDirectory(string root, string application);
    ToolManifest GetManifest(string root, string application);
}