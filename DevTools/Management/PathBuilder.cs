using System;
using System.Collections.Generic;
using DevTools.Catalog;

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

        foreach (var includePath in version.Manifest.Paths)
            values.Add(ManifestVariables.Expand(includePath, version.Path));
    }
}