using System;
using DevTools.Apps;
using Mono.Options;

namespace DevTools
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            var appOptions = new AppOptions();

            var p = new OptionSet
            {
                {"p|path", "get environment path", i => appOptions.Path = true},
                {"c|choice=", "choice app variant.\n", (i, j) =>
                {
                    appOptions.Choice = true;
                    appOptions.AppName = i;
                    appOptions.VariantName = j;
                }},
                {"s|save", "app variant.\n", i => appOptions.Save = true},
                {"h|help", "show this message and exit", ShowHelp}
            };

            try
            {
                var extra = p.Parse(args);
            }
            catch (OptionException e)
            {
                Console.Write("greet: ");
                Console.WriteLine(e.Message);
                Console.WriteLine("Try `greet --help' for more information.");
            }

            var config = AppCollection.FromFile("config.json");
            var appManager = new AppManager("config.json");
        }

        private static void ShowHelp(string s)
        {
        }
    }
}
