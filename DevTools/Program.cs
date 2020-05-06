using System.IO;
using DevTools.Apps;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace DevTools
{
    internal class Program
    {
        private static readonly string RepoFile = Path.Combine(Common.AssemblyDirectory, "config.json");
        private static readonly string PathFile = Path.Combine(Common.AssemblyDirectory, "path.txt");

        private static void Main(string[] args)
        {
            var manager = new AppManager(RepoFile, PathFile);

            var rootCommand = new RootCommand("Manages list of development tool. Allows easy switching between different versions of the same app.");
            rootCommand.AddCommand(BuildUpdateCommand(manager));
            rootCommand.AddCommand(BuildListCommand(manager));
            rootCommand.AddCommand(BuildSelectCommand(manager));
            rootCommand.AddCommand(BuildDisableCommand(manager));
            rootCommand.AddCommand(BuildEnableCommand(manager));
            rootCommand.AddCommand(BuildNewCommand(manager));
            rootCommand.Invoke(args);
        }

        private static Command BuildUpdateCommand(AppManager manager)
        {
            return new Command("update", "Regenerates path.txt.")
            {
                Handler = CommandHandler.Create(manager.UpdatePath)
            };
        }

        private static Command BuildListCommand(AppManager manager)
        {
            return new Command("list", "List available applications.")
            {
                Handler = CommandHandler.Create(manager.ListApps)
            };
        }

        private static Command BuildSelectCommand(AppManager manager)
        {
            var cmd =  new Command("select", "Selects app variant to use.")
            {
                Handler = CommandHandler.Create((string app, string variant, bool save) =>
                {
                    manager.SelectVariant(app, variant);
                    if(save)
                        manager.Repository.SaveAs(RepoFile); //todo
                })
            };
            
            cmd.AddArgument(new Argument<string>("app"));
            cmd.AddArgument(new Argument<string>("variant"));
            cmd.AddOption(new Option<bool>(new []{"-s", "--save"}, "Save selected variant."));

            return cmd;
        }

        private static Command BuildDisableCommand(AppManager manager)
        {
            var cmd = new Command("disable", "Enables application")
            {
                Handler = CommandHandler.Create((string app) => manager.SetDisabled(app, true))
            };

            cmd.AddArgument(new Argument<string>("app"));

            return cmd;
        }

        private static Command BuildEnableCommand(AppManager manager)
        {
            var cmd = new Command("enable", "Enables application")
            {
                Handler = CommandHandler.Create((string app) => manager.SetDisabled(app, false))
            };

            cmd.AddArgument(new Argument<string>("app"));

            return cmd;
        }

        private static Command BuildNewCommand(AppManager manager)
        {
            var cmd = new Command("new", "Creates new app or variant.");

            cmd.AddCommand(BuildNewAppCommand(manager));
            cmd.AddCommand(BuildNewVariantCommand(manager));

            return cmd;
        }

        private static Command BuildNewAppCommand(AppManager manager)
        {
            var cmd = new Command("app", "Creates new app.")
            {
                Handler = CommandHandler.Create((string app, string description, string path) =>
                {
                    manager.AddApp(app, description, path);
                })
            };
            
            cmd.AddArgument(new Argument<string>("app"));
            cmd.AddArgument(new Argument<string>("description"));
            cmd.AddArgument(new Argument<string>("path"));

            return cmd;
        }

        private static Command BuildNewVariantCommand(AppManager manager)
        {
            var cmd = new Command("variant", "Creates new variant.")
            {
                Handler = CommandHandler.Create((string app, string variant, string path) =>
                {
                    manager.AddVariant(app, variant, path);
                })
            };

            cmd.AddArgument(new Argument<string>("app"));
            cmd.AddArgument(new Argument<string>("variant"));
            cmd.AddArgument(new Argument<string>("path"));

            return cmd;
        }
    }
}
