namespace DevTools.Management;

public interface IToolManager
{
    void GetEnv(Shell shell);
    void WriteEnvScript();
    void Prune();
    void Install(string application, string version, bool refresh = false, string source = null);
    void Update(string application, bool refresh = false);
    void UpdateAll(bool refresh = false);
    void Restore(bool refresh = false);
    void Reset(string application, string source = null);
    void Remove(string application, string version);
    void List(bool all);
    void Show(string application);
    void Select(string application, string version);
    void SetDisabled(string application, bool isDisabled);
}