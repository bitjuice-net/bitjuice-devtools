using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace DevTools.Apps
{
    public class AppRepository
    {
        public string Path { get; set; }
        public Dictionary<string, AppDeclaration> Apps { get; set; }

        public static AppRepository FromFile(string fileName)
        {
            return JToken.Parse(File.ReadAllText(fileName)).ToObject<AppRepository>();
        }

        public void SaveAs(string fileName)
        {
            File.WriteAllText(fileName, JToken.FromObject(this).ToString());
        }
    }
}