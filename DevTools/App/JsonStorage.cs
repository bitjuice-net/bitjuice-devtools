using System.IO;
using System.Text.Json;

namespace DevTools.App
{
    public class JsonStorage : IStorage
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public T Load<T>(string fileName) where T : new()
        {
            return File.Exists(fileName) ? JsonSerializer.Deserialize<T>(File.ReadAllText(fileName), Options) : new T();
        }

        public void Save<T>(string fileName, T obj) where T : new()
        {
            File.WriteAllText(fileName, JsonSerializer.Serialize(obj, Options));
        }
    }
}
