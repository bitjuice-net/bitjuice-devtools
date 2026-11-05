namespace DevTools.App
{
    public interface IToolManager
    {
        void GetSetup();
        void GetPath();
        void GetEnvs();
        void Discover();
        void Prune();
        void Install(string application, string version);
        void Update(string application);
        void List(bool all);
        void Select(string application, string version);
        void SetDisabled(string application, bool isDisabled);
    }
}