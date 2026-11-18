namespace DevTools.Infrastructure;

public interface IStorage
{
    T Load<T>(string fileName) where T : new();
    bool Exists(string fileName);
    // changesState is false for files that do not change PATH or variables, so shells do not reload for them.
    void Save<T>(string fileName, T obj, bool changesState = true) where T : new();
}