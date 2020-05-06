using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConsoleTables;

namespace DevTools.Apps
{
    public class AppManager
    {
        private const string EmptyConst = "<empty>";
        private const string NotSetConst = "<not set>";

        private readonly string repoFile;
        private readonly string pathFile;
        private readonly AppRepository repository;

        public AppManager(string repoFile, string pathFile)
        {
            this.repoFile = repoFile;
            this.pathFile = pathFile;
            repository = AppRepository.FromFile(repoFile);
        }

        public void UpdatePath()
        {
            var paths = new List<string>();

            var basePath = Path.IsPathRooted(repository.Path) 
                ? repository.Path 
                : Path.Combine(Common.AssemblyDirectory, repository.Path);

            foreach (var app in repository.Apps.Values)
            {
                if(app.Disabled)
                    continue;
                
                if(app.Variants == null || app.Selected == null)
                    continue;

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
            Console.WriteLine($"Switching {app.Description} from {app.Selected ?? NotSetConst} to {variantName}");
            app.Selected = variantName;

            Save();
        }

        public void ListApps()
        {
            ListApps(string.Empty);
        }

        public void ListApps(string appName)
        {
            var table = new ConsoleTable("Name", "Description", "Disabled", "Variant", "Available");
            foreach (var (key, value) in repository.Apps)
            {
                var versions = value.Variants != null ? string.Join(", ", value.Variants.Keys) : EmptyConst;
                table.AddRow(key, value.Description, value.Disabled, value.Selected ?? NotSetConst, versions);
            }

            Console.WriteLine("List of applications:");
            Console.WriteLine();
            table.Write(Format.MarkDown);
        }
        
        public void SetDisabled(string appName, bool disabled)
        {
            if (!repository.Apps.TryGetValue(appName, out var app))
                throw new Exception($"App not found: {appName}");
            app.Disabled = disabled;

            Save();
        }

        public void AddApp(string appName, string description, string path)
        {
            if (repository.Apps.TryGetValue(appName, out var app))
                throw new Exception($"App already exists: {appName}");

            repository.Apps.Add(appName, new AppDeclaration()
            {
                Description = description,
                Path = path
            });

            Save();
        }

        public void AddVariant(string appName, string variantName, string path)
        {
            if (!repository.Apps.TryGetValue(appName, out var app))
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
            repository.SaveAs(repoFile);
        }
    }
}