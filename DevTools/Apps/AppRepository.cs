using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace DevTools.Apps
{
    public class AppRepository
    {
        public Dictionary<string, AppDeclaration> Apps { get; set; }

        public static AppRepository FromFile(string fileName)
        {
            return new AppRepository
            {
                Apps = JToken.Parse(File.ReadAllText(fileName)).ToObject<Dictionary<string, AppDeclaration>>()
            };
        }

        public void SaveAs(string fileName)
        {
            File.WriteAllText(JToken.FromObject(Apps).ToString(), fileName);
        }
    }
}