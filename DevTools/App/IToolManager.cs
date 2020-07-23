namespace DevTools.App
{
    public interface IToolManager
    {
        void GetPath();
        void Discover();
        void List();
        void Select(string application, string version);
        void SetDisabled(string application, bool isDisabled);
    }
}