using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace DevTools.Infrastructure;

public class JsonStorage : IStorage
{
    private readonly string stateFile;

    // Set by the first save that changes the state, devtools then writes the scripts the shells load.
    public bool StateChanged { get; private set; }

    public JsonStorage() : this(PathUtils.StateFile)
    {
    }

    public JsonStorage(string stateFile)
    {
        this.stateFile = stateFile;
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        AllowOutOfOrderMetadataProperties = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        TypeInfoResolver = JsonTypeInfoResolver.Combine(StorageJsonContext.Default, new DefaultJsonTypeInfoResolver())
    };

    public T Load<T>(string fileName) where T : new()
    {
        return File.Exists(fileName) ? JsonSerializer.Deserialize<T>(File.ReadAllText(fileName), Options) : new T();
    }

    public bool Exists(string fileName)
    {
        return File.Exists(fileName);
    }

    // For downloaded content that is checked before it is written anywhere.
    public static T Parse<T>(byte[] json)
    {
        return JsonSerializer.Deserialize<T>(json, Options);
    }

    // A save also writes a new stamp to the state file, so shells reload PATH and variables only after a change.
    public void Save<T>(string fileName, T obj, bool changesState = true) where T : new()
    {
        var directory = Path.GetDirectoryName(fileName)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(fileName, JsonSerializer.Serialize(obj, Options));

        if (changesState)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
            File.WriteAllText(stateFile, DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
            StateChanged = true;
        }
    }

    public static string ReadState(string stateFile)
    {
        return File.Exists(stateFile) ? File.ReadAllText(stateFile).Trim() : "";
    }
}