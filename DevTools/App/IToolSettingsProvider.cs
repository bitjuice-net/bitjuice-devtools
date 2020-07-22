namespace DevTools.App
{
    public interface IToolSettingsProvider
    {
        ToolSettings GetSettings(string name);
    }
}