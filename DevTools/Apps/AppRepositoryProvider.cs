using System.IO;
using Newtonsoft.Json.Linq;

namespace DevTools.Apps
{
    public class AppRepositoryProvider : IAppRepositoryProvider
    {
        private readonly string fileName;

        public AppRepositoryProvider(string fileName)
        {
            this.fileName = fileName;
        }

        public AppRepository Load()
        {
            return JToken.Parse(File.ReadAllText(fileName)).ToObject<AppRepository>();
        }

        public void Save(AppRepository repository)
        {
            File.WriteAllText(fileName, JToken.FromObject(repository).ToString());
        }
    }
}