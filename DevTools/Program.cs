using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using DevTools.App;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevTools
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            var configuration = new ConfigurationBuilder().AddJsonFile("settings.json").Build();
            var manager = BuildIoC(configuration).GetService<IToolManager>();
            var command = BuildCommands(manager);
            
            command.Invoke(args);
        }

        public static IServiceProvider BuildIoC(IConfigurationRoot configuration)
        {
            var services = new ServiceCollection();

            services.Configure<Settings>(configuration);

            services.AddSingleton<IStorage, JsonStorage>();
            services.AddSingleton<IToolManager, ToolManager>();
            services.AddSingleton<IToolDefinitionProvider, ToolDefinitionProvider>();
            services.AddSingleton<IToolSettingsProvider, ToolSettingsProvider>();

            return services.BuildServiceProvider();
        }

        private static RootCommand BuildCommands(IToolManager manager)
        {
            var cmd = new RootCommand("Manages list of development tool. Allows easy switching between different versions of the same app.");

            cmd.AddCommand(BuildDiscoverCommand(manager));
            cmd.AddCommand(BuildSetupCommand(manager));
            cmd.AddCommand(BuildPathCommand(manager));
            cmd.AddCommand(BuildEnvsCommand(manager));
            cmd.AddCommand(BuildListCommand(manager));
            cmd.AddCommand(BuildSelectCommand(manager));
            cmd.AddCommand(BuildDisableCommand(manager));
            cmd.AddCommand(BuildEnableCommand(manager));

            return cmd;
        }

        private static Command BuildSetupCommand(IToolManager manager)
        {
            var cmd = new Command("setup", "Get setup string.")
            {
                Handler = CommandHandler.Create(manager.GetSetup)
            };

            return cmd;
        }

        private static Command BuildPathCommand(IToolManager manager)
        {
            var cmd = new Command("path", "Get PATH string.")
            {
                Handler = CommandHandler.Create(manager.GetPath)
            };
            
            return cmd;
        }

        private static Command BuildEnvsCommand(IToolManager manager)
        {
            var cmd = new Command("envs", "Get environment variables.")
            {
                Handler = CommandHandler.Create(manager.GetEnvs)
            };

            return cmd;
        }

        private static Command BuildDiscoverCommand(IToolManager manager)
        {
            var cmd = new Command("discover", "Discover applications.")
            {
                Handler = CommandHandler.Create(manager.Discover)
            };

            return cmd;
        }

        private static Command BuildListCommand(IToolManager manager)
        {
            var cmd = new Command("list", "List available applications.")
            {
                Handler = CommandHandler.Create(manager.List)
            };

            cmd.AddAlias("ls");

            return cmd;
        }

        private static Command BuildSelectCommand(IToolManager manager)
        {
            var cmd = new Command("select", "Select application version.")
            {
                Handler = CommandHandler.Create((string application, string version) =>
                {
                    manager.Select(application, version);
                })
            };

            cmd.AddAlias("s");
            cmd.AddArgument(new Argument<string>("application"));
            cmd.AddArgument(new Argument<string>("version")
            {
                Arity = new ArgumentArity(0, 1)
            });

            return cmd;
        }

        private static Command BuildDisableCommand(IToolManager manager)
        {
            var cmd = new Command("disable", "Disable application.")
            {
                Handler = CommandHandler.Create((string application) => manager.SetDisabled(application, true))
            };

            cmd.AddAlias("d");
            cmd.AddArgument(new Argument<string>("application"));

            return cmd;
        }

        private static Command BuildEnableCommand(IToolManager manager)
        {
            var cmd = new Command("enable", "Enable application.")
            {
                Handler = CommandHandler.Create((string application) => manager.SetDisabled(application, false))
            };

            cmd.AddAlias("e");
            cmd.AddArgument(new Argument<string>("application"));

            return cmd;
        }
    }
}
