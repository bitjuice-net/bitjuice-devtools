using System;
using System.Collections.Generic;
using System.Linq;
using DevTools.Catalog;
using DevTools.Infrastructure;

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

        Environment.SetEnvironmentVariable("APPDIR", version.Path);

        foreach (var (key, value) in version.Manifest.Envs) 
            values.Add(new(key, Environment.ExpandEnvironmentVariables(value)));
    }

    public string Build()
    {
        return values.Select(i => $"{i.Key}={i.Value}").Join(Environment.NewLine);
    }
}