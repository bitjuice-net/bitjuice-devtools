using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DevTools.Apps
{
    public class AppManager
    {
        private readonly string fileName;
        private readonly AppCollection apps;

        public AppManager(string fileName)
        {
            if (!File.Exists(fileName))
                throw new FileNotFoundException("Configuration file is missing.", Path.GetFullPath(fileName));
            this.fileName = fileName;
            apps = AppCollection.FromFile(fileName);
        }

        public void GetPath()
        {
            var paths = new List<string>();
            foreach (var app in apps.Values)
            {
                if (!app.Variants.TryGetValue(app.Selected, out var variant))
                    variant = app.Variants.Values.FirstOrDefault();
                if(variant == null)
                    continue;
                foreach (var path in variant.Paths)
                {
                    paths.Add(Path.GetFullPath(Path.Combine(app.BasePath, path)));
                }
            }
            Console.WriteLine(string.Join(";", paths));
        }

        public void SetDefault(string appName, string variantName)
        {
            if(!apps.TryGetValue(appName, out var app))
                return;
            if(!app.Variants.TryGetValue(variantName, out var variant))
                return;
            app.Selected = variantName;
        }

        public void List(string appName)
        {
            foreach (var app in apps)
            {
                Console.WriteLine($"{app.Key}: {app.Value.Description}");
                foreach (var variant in app.Value.Variants)
                    Console.WriteLine($"\t{variant.Key}: {variant.Value.Description}");
            }
        }
    }
}