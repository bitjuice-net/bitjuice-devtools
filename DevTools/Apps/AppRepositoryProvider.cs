using System.IO;
using System.Text.Json;

namespace DevTools.Apps
{
    public class AppRepositoryProvider : IAppRepositoryProvider
    {
        private readonly JsonSerializerOptions options;
        private readonly string fileName;

        public AppRepositoryProvider(string fileName)
        {
            options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };
            this.fileName = fileName;
        }

        public AppRepository Load()
        {
            return JsonSerializer.Deserialize<AppRepository>(File.ReadAllText(fileName), options);
        }

        public void Save(AppRepository repository)
        {
            File.WriteAllText(fileName, JsonSerializer.Serialize(repository, options));
        }
    }
}