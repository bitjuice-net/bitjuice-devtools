using System.IO;
using System.Text.Json;

namespace DevTools.New
{
    public class JsonEx
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public static T DeserializeFromFile<T>(string fileName)
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(fileName), Options);
        }

        public static void SerializeToFile<T>(string fileName, T obj)
        {
            File.WriteAllText(fileName, JsonSerializer.Serialize(obj, Options));
        }
    }
}