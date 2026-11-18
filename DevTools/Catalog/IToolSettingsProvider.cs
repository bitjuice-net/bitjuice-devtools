using System;
using System.Collections.Generic;
using DevTools.Catalog.Models;

namespace DevTools.Catalog;

public interface IToolSettingsProvider
{
    ToolSettings GetSettings(string application);
    void UpdateSettings(string application, Action<ToolSettings> action);
    List<string> GetApplications();
    void RemoveSettings(string application);
}