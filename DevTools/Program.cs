using System;
using System.Collections.Generic;
using DevTools.Apps;
using Mono.Options;

namespace DevTools
{
    internal class Program
    {
        private const string ConfigFile = "config.json";

        private static void Main(string[] args)
        {
            var options = GetOptions(args);
            if (options != null)
                Execute(options);
        }

        private static void Execute(AppOptions options)
        {
            var repository = AppRepository.FromFile(ConfigFile);
            var manager = new AppManager(repository);

            foreach (var variant in options.Variants)
                manager.SelectVariant(variant.AppName, variant.VariantName);

            if (options.List)
                manager.ListApps("");

            if (options.Path)
                manager.PrintPath();

            if (options.Save)
                repository.SaveAs(ConfigFile);
        }

        private static AppOptions GetOptions(IEnumerable<string> args)
        {
            var result = new AppOptions();
            var help = false;
            var optionSet = new OptionSet
            {
                {"p|path", "Print generated PATH", i => result.Path = true},
                {"v|variant=", "Set application variant.\n", (app, variant) => result.AddVariant(app, variant)},
                {"l|list", "List available applications.\n", i => result.Save = true},
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
