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

        return files.TryGetValue(Key(fileName), out var value) ? (T)value : new T();
    }

    public bool Exists(string fileName)
    {
        return files.ContainsKey(Key(fileName));
    }

    public void Save<T>(string fileName, T obj, bool changesState = true) where T : new()
    {
        files[Key(fileName)] = obj;
    }

    // config/apps.json and cache/apps.json are different files, so the directory is part of the key.
    private static string Key(string fileName)
    {
        return Path.GetFileName(Path.GetDirectoryName(fileName)) + "/" + Path.GetFileName(fileName);
    }
}
