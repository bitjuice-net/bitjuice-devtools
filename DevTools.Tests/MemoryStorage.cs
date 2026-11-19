using System.Collections.Generic;
using System.IO;
using DevTools.Infrastructure;

namespace DevTools.Tests;

internal class MemoryStorage : IStorage
{
    private readonly Dictionary<string, object> files = new();

    public T Load<T>(string fileName) where T : new()
    {
        if (Path.GetFileName(fileName) is ".app")
            return new JsonStorage().Load<T>(fileName);

        return files.TryGetValue(Path.GetFileName(fileName), out var value) ? (T)value : new T();
    }

    public bool Exists(string fileName)
    {
        return files.ContainsKey(Path.GetFileName(fileName));
    }

    public void Save<T>(string fileName, T obj, bool changesState = true) where T : new()
    {
        files[Path.GetFileName(fileName)] = obj;
    }
}
