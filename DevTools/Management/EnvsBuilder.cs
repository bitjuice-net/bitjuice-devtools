using System;
using System.Collections.Generic;
using DevTools.Catalog;

namespace DevTools.Management;

public class EnvsBuilder
{
    private readonly List<KeyValuePair<string, string>> values = new();

    public IReadOnlyList<KeyValuePair<string, string>> Values => values;

    public void AddApplication(ToolDefinition version)
    {
        if (version == null)
            throw new ArgumentNullException(nameof(version));

        if (version.Manifest.Envs == null)
            return;

        foreach (var (key, value) in version.Manifest.Envs)
            values.Add(new(key, ManifestVariables.Expand(value, version.Path)));
    }
}