using System.IO;
using DevTools.Apps;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace DevTools
{
    internal class Program
    {
        private static readonly string RepoFile = Path.Combine(Common.AssemblyDirectory, "config.json");

        private static void Main(string[] args)
        {
            var manager = new AppManager(new AppRepositoryProvider(RepoFile));
            var cmd = BuildCommands(manager);
            cmd.Invoke(args);
        }

        private static RootCommand BuildCommands(AppManager manager)
        {
            var cmd = new RootCommand("Manages list of development tool. Allows easy switching between different versions of the same app.");

            cmd.AddCommand(BuildPathCommand(manager));
            cmd.AddCommand(BuildListCommand(manager));
            cmd.AddCommand(BuildSelectCommand(manager));
            cmd.AddCommand(BuildDisableCommand(manager));
            cmd.AddCommand(BuildEnableCommand(manager));
            cmd.AddCommand(BuildAddCommand(manager));
            
            return cmd;
        }

        private static Command BuildPathCommand(AppManager manager)
        {
            var cmd = new Command("path", "Get PATH string.")
            {
                Handler = CommandHandler.Create(manager.GetPath)
            };
            
            cmd.AddAlias("p");

            return cmd;
        }

        private static Command BuildListCommand(AppManager manager)
        {
            var cmd = new Command("list", "List available applications.")
            {
                Handler = CommandHandler.Create(manager.ListApps)
            };

            cmd.AddAlias("l");

            return cmd;
        }

        private static Command BuildSelectCommand(AppManager manager)
        {
            var cmd =  new Command("select", "Select app variant to use.")
            {
                Handler = CommandHandler.Create((string appName, string variantName) =>
                {
                    manager.SelectVariant(appName, variantName);
                })
            };

            cmd.AddAlias("s");
            cmd.AddArgument(new Argument<string>("app-name"));
            cmd.AddArgument(new Argument<string>("variant-name"));

            return cmd;
        }

        private static Command BuildDisableCommand(AppManager manager)
        {
            var cmd = new Command("disable", "Disable application.")
            {
                Handler = CommandHandler.Create((string appName) => manager.SetDisabled(appName, true))
            };

            cmd.AddAlias("d");
            cmd.AddArgument(new Argument<string>("app-name"));

            return cmd;
        }

        private static Command BuildEnableCommand(AppManager manager)
        {
            var cmd = new Command("enable", "Enable application.")
            {
                Handler = CommandHandler.Create((string appName) => manager.SetDisabled(appName, false))
            };

            cmd.AddAlias("e");
            cmd.AddArgument(new Argument<string>("app-name"));

            return cmd;
        }

        private static Command BuildAddCommand(AppManager manager)
        {
            var cmd = new Command("add", "Add new app or variant.");

            cmd.AddAlias("a");
            cmd.AddCommand(BuildNewAppCommand(manager));
            cmd.AddCommand(BuildNewVariantCommand(manager));

            return cmd;
        }

        private static Command BuildNewAppCommand(AppManager manager)
        {
            var cmd = new Command("app", "Add new app.")
            {
                Handler = CommandHandler.Create((string appName, string description, string path) =>
                {
                    manager.AddApp(appName, description, path);
                })
            };
            
            cmd.AddArgument(new Argument<string>("app-name"));
            cmd.AddArgument(new Argument<string>("description"));
            cmd.AddArgument(new Argument<string>("path"));

            return cmd;
        }

        private static Command BuildNewVariantCommand(AppManager manager)
        {
            var cmd = new Command("variant", "Add new variant.")
            {
                Handler = CommandHandler.Create((string appName, string variantName, string[] paths) =>
                {
                    manager.AddVariant(appName, variantName, paths);
                })
            };

            cmd.AddArgument(new Argument<string>("app-name"));
            cmd.AddArgument(new Argument<string>("variant-name"));
            cmd.AddArgument(new Argument<string>("paths")
            {
                Arity = new ArgumentArity(1, short.MaxValue)
            });

            return cmd;
        }
    }
}
