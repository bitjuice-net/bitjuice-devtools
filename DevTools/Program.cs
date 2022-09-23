using System;
using System.CommandLine;
using DevTools.App;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevTools
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile("settings.json")
                .Build();

            BuildApp(config)
                .GetRequiredService<ICommandFactory>()
                .CreateRootCommand()
                .Invoke(args);
        }

        public static IServiceProvider BuildApp(IConfigurationRoot configuration)
        {
            var services = new ServiceCollection();

            services.Configure<Settings>(configuration);

            services.AddSingleton<ICommandFactory, CommandFactory>();
            services.AddSingleton<IStorage, JsonStorage>();
            services.AddSingleton<IToolManager, ToolManager>();
            services.AddSingleton<IToolDefinitionProvider, ToolDefinitionProvider>();
            services.AddSingleton<IToolSettingsProvider, ToolSettingsProvider>();

            return services.BuildServiceProvider();
        }
    }
}
