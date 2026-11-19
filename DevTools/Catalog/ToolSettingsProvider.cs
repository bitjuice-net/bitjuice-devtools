using System;
using System.Collections.Generic;
using System.Linq;
using DevTools.Catalog.Models;
using DevTools.Infrastructure;

namespace DevTools.Catalog;

public class ToolSettingsProvider(IStorage storage, IToolDefinitionProvider definitionProvider)
    : IToolSettingsProvider
{
    private static readonly string FileName = PathUtils.GetConfigPath("apps.json");

    private Dictionary<string, ToolSettings> applications;
    private Dictionary<string, ToolSettings> Applications => applications ??= storage.Load<Dictionary<string, ToolSettings>>(FileName);

    public ToolSettings GetSettings(string application)
    {
        if (Applications.TryGetValue(application, out var config))
            return config;

        // Not stored until it changes, so an application without versions yet gets latest once it has some.
        return new ToolSettings { Version = definitionProvider.GetVersions(application).Any() ? MetaVersion.Latest : null };
    }

    public void UpdateSettings(string application, Action<ToolSettings> action)
    {
        var settings = GetSettings(application);
        action(settings);
        Applications[application] = settings;
        storage.Save(FileName, Applications);
    }

    public List<string> GetApplications()
    {
        return Applications.Keys.ToList();
    }

    public void RemoveSettings(string application)
    {
        if (Applications.Remove(application))
            storage.Save(FileName, Applications);
    }
}