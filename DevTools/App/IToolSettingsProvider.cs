using System;

namespace DevTools.App
{
    public interface IToolSettingsProvider
    {
        ToolSettings GetSettings(string application);
        void SetSettings(string application, ToolSettings settings);
        void UpdateSettings(string application, Action<ToolSettings> action);
    }
}