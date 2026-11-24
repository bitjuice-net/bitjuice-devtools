using System.Collections.Generic;
using DevTools.Catalog.Models;

namespace DevTools.Catalog;

public interface IToolDefinitionProvider
{
    List<string> GetNames();
    List<ToolDefinition> GetVersions(string application);
    ToolDefinition GetVersion(string application, string version);

    // The repository the application was fetched from, null for one made by hand.
    string GetSource(string application);

    // Directories of the tools directory without .app.
    List<string> GetUnmanaged();
    void Scan(string root);
    string FindSourceDirectory(string root, string application);
    string FindDirectory(string root, string application);
    ToolManifest GetManifest(string root, string application);
}