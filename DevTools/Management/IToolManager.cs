namespace DevTools.Management;

public interface IToolManager
{
    void GetEnv(Shell shell);
    void Scan();
    void Prune();
    void Install(string application, string version, bool refresh = false);
    void Update(string application, bool refresh = false);
    void UpdateAll(bool refresh = false);
    void Remove(string application, string version);
    void List(bool all);
    void Show(string application);
    void Select(string application, string version);
    void SetDisabled(string application, bool isDisabled);
}