using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConsoleTables;

namespace DevTools.Apps
{
    public class AppManager
    {
        private readonly AppRepository repository;

        public AppManager(AppRepository repository)
        {
            this.repository = repository;
        }

        public void UpdatePath(string pathFile)
        {
            var paths = new List<string>();

            var basePath = Path.IsPathRooted(repository.Path) 
                ? repository.Path 
                : Path.Combine(Common.AssemblyDirectory, repository.Path);

            foreach (var app in repository.Apps.Values)
            {
                if (!app.Variants.TryGetValue(app.Selected, out var variant))
                    variant = app.Variants.Values.FirstOrDefault();
                if(variant == null)
                    continue;

                var appPath = Path.Combine(basePath, app.Path ?? string.Empty);

                foreach (var variantPath in variant.Paths)
                    paths.Add(Path.GetFullPath(Path.Combine(appPath, variantPath)));
            }

            var path = string.Join(";", paths);
            File.WriteAllText(pathFile, path);
        }

        public void SelectVariant(string appName, string variantName)
        {
            if(!repository.Apps.TryGetValue(appName, out var app))
                throw new Exception($"App not found: {appName}");
            if(!app.Variants.TryGetValue(variantName, out var variant))
                throw new Exception($"Variant not found: {variantName}");
            Console.WriteLine($"Switching {app.Description} from {app.Selected} to {variantName}");
            app.Selected = variantName;
        }

        public void ListApps(string appName)
        {
            var table = new ConsoleTable("Name", "Description", "Variant", "Available");
            foreach (var (key, value) in repository.Apps)   
                table.AddRow(key, value.Description, value.Selected, string.Join(", ", value.Variants.Keys));
            Console.WriteLine("List of applications:");
            Console.WriteLine();
            table.Write(Format.MarkDown);
        }
    }
}