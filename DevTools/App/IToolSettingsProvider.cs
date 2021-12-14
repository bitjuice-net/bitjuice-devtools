using System;

namespace DevTools.App
{
    public interface IToolSettingsProvider
    {
        ToolSettings GetSettings(string application);
        void UpdateSettings(string application, Action<ToolSettings> action);
    }
}