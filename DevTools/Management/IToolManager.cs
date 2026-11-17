namespace DevTools.Management;

public interface IToolManager
{
    void GetEnv(Shell shell);
    void Scan();
    void Prune();
    void Install(string application, string version);
    void Update(string application);
    void Remove(string application, string version);
    void List(bool all);
    void Select(string application, string version);
    void SetDisabled(string application, bool isDisabled);
}