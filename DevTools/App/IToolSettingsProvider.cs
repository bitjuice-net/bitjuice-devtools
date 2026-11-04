using System;
using System.Collections.Generic;

namespace DevTools.App
{
    public interface IToolSettingsProvider
    {
        ToolSettings GetSettings(string application);
        void UpdateSettings(string application, Action<ToolSettings> action);
        List<string> GetApplications();
        void RemoveSettings(string application);
    }
}