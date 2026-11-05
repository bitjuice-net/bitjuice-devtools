using System;
using System.CommandLine;
using System.Net.Http;
using DevTools.Catalog;
using DevTools.Cli;
using DevTools.Infrastructure;
using DevTools.Installation;
using DevTools.Management;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevTools
{
    internal class Program
    {
        private static int Main(string[] args)
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile("settings.json")
                .AddJsonFile("settings.local.json", optional: true)
                .Build();

            try
            {
                return BuildApp(config)
                    .GetRequiredService<ICommandFactory>()
                    .CreateRootCommand()
                    .Parse(args)
                    .Invoke(new InvocationConfiguration { EnableDefaultExceptionHandler = false });
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Error: {e.Message}");
                return 1;
            }
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
            services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromMinutes(30) });
            services.AddSingleton<IVersionSource, GitHubVersionSource>();
            services.AddSingleton<IDownloader, HttpDownloader>();
            services.AddSingleton<IToolInstaller, ToolInstaller>();

            return services.BuildServiceProvider();
        }
    }
}
