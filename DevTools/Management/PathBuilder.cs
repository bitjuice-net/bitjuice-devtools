using System;
using System.Collections.Generic;
using System.IO;
using DevTools.Catalog;
using DevTools.Infrastructure;

namespace DevTools.Management;

public class PathBuilder
{
    private readonly List<string> values = new();

    public IReadOnlyList<string> Values => values;

    public void AddApplication(ToolDefinition version)
    {
        if (version == null) 
            throw new ArgumentNullException(nameof(version));

        if (version.Manifest.Paths == null)
            return;

        Environment.SetEnvironmentVariable("APPDIR", version.Path);

        // Manifests are usually written with \, which is not a separator outside of Windows.
        foreach (var includePath in version.Manifest.Paths)
            values.Add(Environment.ExpandEnvironmentVariables(OperatingSystem.IsWindows() ? includePath : includePath.Replace('\\', '/')));
    }

    public string Build()
    {
        return values.Join(Path.PathSeparator);
    }
}