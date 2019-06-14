using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DevTools.Apps
{
    public class AppManager
    {
        private readonly AppRepository repository;

        public AppManager(AppRepository repository)
        {
            this.repository = repository;
        }

        public void PrintPath()
        {
            var paths = new List<string>();
            foreach (var app in repository.Apps.Values)
            {
                if (!app.Variants.TryGetValue(app.Selected, out var variant))
                    variant = app.Variants.Values.FirstOrDefault();
                if(variant == null)
                    continue;
                foreach (var path in variant.Paths)
                {
                    paths.Add(Path.GetFullPath(Path.Combine(app.Path ?? string.Empty, path)));
                }
            }
            Console.WriteLine(string.Join(";", paths));
        }

        public void SelectVariant(string appName, string variantName)
        {
            if(!repository.Apps.TryGetValue(appName, out var app))
                throw new Exception($"App not found: {appName}");
            if(!app.Variants.TryGetValue(variantName, out var variant))
                throw new Exception($"Variant not found: {variantName}");
            app.Selected = variantName;
        }

        public void ListApps(string appName)
        {
            foreach (var app in repository.Apps)
            {
                var variants = string.Join(", ", app.Value.Variants.Keys);
                Console.WriteLine($"{app.Key.PadRight(10)}: {app.Value.Description.PadRight(20)} ({variants})");
            }
        }
    }
}