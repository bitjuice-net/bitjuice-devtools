using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace DevTools.Infrastructure;

public class JsonStorage : IStorage
{
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

    public void Save<T>(string fileName, T obj) where T : new()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(fileName)!);
        File.WriteAllText(fileName, JsonSerializer.Serialize(obj, Options));
    }
}