using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace DevTools.Apps
{
    public class AppCollection : Dictionary<string, AppDeclaration>
    {
        public static AppCollection FromFile(string fileName)
        {
            return  JToken.Parse(File.ReadAllText(fileName)).ToObject<AppCollection>();
        }

        public void SaveAs(string fileName)
        {
            File.WriteAllText(JToken.FromObject(this).ToString(), fileName);
        }
    }
}