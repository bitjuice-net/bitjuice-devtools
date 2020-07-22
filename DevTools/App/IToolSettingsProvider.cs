namespace DevTools.New
{
    public interface IToolSettingsProvider
    {
        ToolSettings GetSettings(string name);
    }
}