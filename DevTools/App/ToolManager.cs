using System;
using Microsoft.Extensions.Options;

namespace DevTools.New
{
    public class ToolManager : IToolManager
    {
        private readonly Settings settings;
        private readonly IToolDefinitionProvider toolDefinitionProvider;
        private readonly IToolSettingsProvider toolSettingsProvider;

        public ToolManager(IOptions<Settings> settings, IToolDefinitionProvider toolDefinitionProvider, IToolSettingsProvider toolSettingsProvider)
        {
            this.settings = settings.Value;
            this.toolDefinitionProvider = toolDefinitionProvider;
            this.toolSettingsProvider = toolSettingsProvider;
        }

        public void GetPath()
        {
            var builder = new PathBuilder(PathEx.GetRootedPath(settings.RootPath));

            foreach (var name in toolDefinitionProvider.GetNames())
            {
                var toolSettings = toolSettingsProvider.GetSettings(name);

                if (toolSettings.IsDisabled)
                    continue;

                if (string.IsNullOrWhiteSpace(toolSettings.Version))
                    continue;

                var version = toolDefinitionProvider.GetVersion(name, toolSettings.Version);
                if(version == null)
                    continue;

                builder.AddApplication(version);
            }

            Console.WriteLine(builder.Build());
        }

        public void Discover()
        {
            toolDefinitionProvider.Discover(PathEx.GetRootedPath(settings.RootPath));
        }

        // public void ListApps()
        // {
        //     var table = new ConsoleTable("Name", "Description", "Disabled", "Variant", "Available");
        //
        //     foreach (var (key, value) in repository.Apps)
        //     {
        //         var versions = value.Variants != null ? string.Join(", ", value.Variants.Keys) : EmptyConst;
        //         table.AddRow(key, value.Description, value.Disabled, value.Selected ?? NotSetConst, versions);
        //     }
        //
        //     Console.WriteLine("List of applications:");
        //     Console.WriteLine();
        //
        //     table.Write(Format.MarkDown);
        // }
        //
        // public void SelectVariant(string appName, string variantName)
        // {
        //     if (!repository.Apps.TryGetValue(appName, out var app))
        //         throw new Exception($"App not found: {appName}");
        //
        //     if (string.IsNullOrWhiteSpace(variantName))
        //     {
        //         var variants = app.Variants.ToArray();
        //         for (var i = 0; i < variants.Length; i++)
        //             Console.WriteLine($"[{i}] {variants[i].Key}");
        //         Console.Write("Select variant to use: ");
        //         var input = Console.ReadLine();
        //
        //         if (!int.TryParse(input, out var selected))
        //             throw new Exception($"'{input}' is not a valid index");
        //
        //         if (selected < 0 || selected >= variants.Length)
        //             throw new Exception($"Selected index is out of range");
        //
        //         variantName = variants[selected].Key;
        //     }
        //     else
        //     {
        //         if (!app.Variants.TryGetValue(variantName, out var variant))
        //             throw new Exception($"Variant not found: {variantName}");
        //     }
        //
        //     Console.WriteLine($"Switching {app.Description} from {app.Selected ?? NotSetConst} to {variantName}");
        //     app.Selected = variantName;
        //
        //     provider.Save(repository);
        // }
        //
        //
        // public void SetDisabled(string appName, bool disabled)
        // {
        //     if (!repository.Apps.TryGetValue(appName, out var app))
        //         throw new Exception($"App not found: {appName}");
        //
        //     app.Disabled = disabled;
        //
        //     provider.Save(repository);
        // }
        //
        // public void AddApp(string appName, string description, string path)
        // {
        //     if (repository.Apps.TryGetValue(appName, out var app))
        //         throw new Exception($"App already exists: {appName}");
        //
        //     repository.Apps.Add(appName, new AppDeclaration()
        //     {
        //         Description = description,
        //         Path = path
        //     });
        //
        //     provider.Save(repository);
        // }
        //
        // public void AddVariant(string appName, string variantName, string[] paths)
        // {
        //     if (!repository.Apps.TryGetValue(appName, out var app))
        //         throw new Exception($"App not found: {appName}");
        //
        //     app.Variants ??= new Dictionary<string, AppVariant>();
        //
        //     if (app.Variants.TryGetValue(variantName, out var variant))
        //         variant.Paths.AddRange(paths);
        //     else
        //         app.Variants.Add(variantName, new AppVariant { Paths = paths.ToList() });
        //
        //     provider.Save(repository);
        // }
    }
}
