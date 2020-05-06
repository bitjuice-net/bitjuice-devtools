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

        private readonly IAppRepositoryProvider provider;
        private readonly AppRepository repository;

        public AppManager(IAppRepositoryProvider provider)
        {
            this.provider = provider;
            repository = provider.Load();
        }

        public void GetPath()
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

            Console.WriteLine(string.Join(";", paths));
        }
        
        public void ListApps()
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

        public void SelectVariant(string appName, string variantName)
        {
            if (!repository.Apps.TryGetValue(appName, out var app))
                throw new Exception($"App not found: {appName}");

            if (!app.Variants.TryGetValue(variantName, out var variant))
                throw new Exception($"Variant not found: {variantName}");

            Console.WriteLine($"Switching {app.Description} from {app.Selected ?? NotSetConst} to {variantName}");
            app.Selected = variantName;

            provider.Save(repository);
        }


        public void SetDisabled(string appName, bool disabled)
        {
            if (!repository.Apps.TryGetValue(appName, out var app))
                throw new Exception($"App not found: {appName}");

            app.Disabled = disabled;

            provider.Save(repository);
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

            provider.Save(repository);
        }

        public void AddVariant(string appName, string variantName, string[] paths)
        {
            if (!repository.Apps.TryGetValue(appName, out var app))
                throw new Exception($"App not found: {appName}");

            app.Variants ??= new Dictionary<string, AppVariant>();

            if (app.Variants.TryGetValue(variantName, out var variant))
                variant.Paths.AddRange(paths);
            else
                app.Variants.Add(variantName, new AppVariant {Paths = paths.ToList()});

            provider.Save(repository);
        }
    }
}