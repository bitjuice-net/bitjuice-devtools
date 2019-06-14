using System;
using System.Collections.Generic;
using System.IO;
using DevTools.Apps;
using Mono.Options;

namespace DevTools
{
    internal class Program
    {
        private const string ConfigFile = "config.json";
        private const string PathFile = "path.txt";

        private static void Main(string[] args)
        {
            var configFile = Path.Combine(Common.AssemblyDirectory, ConfigFile);
            var pathFile = Path.Combine(Common.AssemblyDirectory, PathFile);

            var options = GetOptions(args);
            if (options != null)
                Execute(options, configFile, pathFile);
        }

        private static void Execute(AppOptions options, string configFile, string pathFile)
        {
            var repository = AppRepository.FromFile(configFile);
            var manager = new AppManager(repository);

            foreach (var variant in options.Variants)
                manager.SelectVariant(variant.AppName, variant.VariantName);

            if (options.List)
                manager.ListApps("");

            if (options.Save)
                repository.SaveAs(configFile);

            if (options.Update)
                manager.UpdatePath(pathFile);
        }

        private static AppOptions GetOptions(IEnumerable<string> args)
        {
            var result = new AppOptions();
            var help = false;
            var optionSet = new OptionSet
            {
                {"u|update", "Update PATH file", i => result.Update = true},
                {"v|variant=", "Set application variant.\n", (app, variant) => result.AddVariant(app, variant)},
                {"l|list", "List available applications.\n", i => result.List = true},
                {"s|save", "Save variant.\n", i => result.Save = true},
                {"h|help", "Show this message and exit", i => help = true}
            };

            try
            {
                optionSet.Parse(args);

                if (!help)
                    return result;

                ShowHelp(optionSet);
                return null;

            }
            catch (OptionException e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine("Try `devtools --help' for more information.");
                return null;
            }
        }

        private static void ShowHelp(OptionSet optionSet)
        {
            Console.WriteLine("Usage: devtools [OPTIONS]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            optionSet.WriteOptionDescriptions(Console.Out);
        }
    }
}
