using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConsoleTables;

namespace DevTools.Apps
{
    public class AppManager
    {
        public string RepoFile { get; }
        public string PathFile { get; }
        public AppRepository Repository { get; }

        public AppManager(string repoFile, string pathFile)
        {
            RepoFile = repoFile;
            PathFile = pathFile;
            Repository = AppRepository.FromFile(repoFile);
        }

        public void UpdatePath()
        {
            var paths = new List<string>();

            var basePath = Path.IsPathRooted(Repository.Path) 
                ? Repository.Path 
                : Path.Combine(Common.AssemblyDirectory, Repository.Path);

            foreach (var app in Repository.Apps.Values)
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
            File.WriteAllText(PathFile, path);
        }

        public void SelectVariant(string appName, string variantName)
        {
            if(!Repository.Apps.TryGetValue(appName, out var app))
                throw new Exception($"App not found: {appName}");
            if(!app.Variants.TryGetValue(variantName, out var variant))
                throw new Exception($"Variant not found: {variantName}");
            Console.WriteLine($"Switching {app.Description} from {app.Selected} to {variantName}");
            app.Selected = variantName;
        }

        public void ListApps()
        {
            ListApps(string.Empty);
        }

        public void ListApps(string appName)
        {
            var table = new ConsoleTable("Name", "Description", "Variant", "Available");
            foreach (var (key, value) in Repository.Apps)
            {
                var versions = value.Variants != null ? string.Join(", ", value.Variants.Keys) : "<empty>";
                table.AddRow(key, value.Description, value.Selected ?? "<not set>", versions);
            }

            Console.WriteLine("List of applications:");
            Console.WriteLine();
            table.Write(Format.MarkDown);
        }

        public void AddApp(string appName, string description, string path)
        {
            if (Repository.Apps.TryGetValue(appName, out var app))
                throw new Exception($"App already exists: {appName}");

            Repository.Apps.Add(appName, new AppDeclaration()
            {
                Description = description,
                Path = path
            });

            Save();
        }

        public void AddVariant(string appName, string variantName, string path)
        {
            if (!Repository.Apps.TryGetValue(appName, out var app))
                throw new Exception($"App not found: {appName}");

            app.Variants ??= new Dictionary<string, AppVariant>();

            if (app.Variants.TryGetValue(variantName, out var variant))
            {
                variant.Paths.Add("path");
            }
            else
            {
                variant = new AppVariant {Paths = new List<string> {path}};
                app.Variants.Add(variantName, variant);
            }

            Save();
        }

        public void Save()
        {
            Repository.SaveAs(RepoFile);
        }
    }
}