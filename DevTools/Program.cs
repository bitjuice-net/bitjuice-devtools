using System;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Net.Http;
using DevTools.Catalog;
using DevTools.Cli;
using DevTools.Infrastructure;
using DevTools.Installation;
using DevTools.Management;
using DevTools.Sources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevTools;

internal class Program
{
    private static int Main(string[] args)
    {
        // Help and parser messages in English like the rest of devtools, not in the language of the system.
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(PathUtils.ConfigDirectoryName, "settings.json"), optional: true)
            .Build();

        var app = BuildApp(config);

        try
        {
            return app
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
        finally
        {
            WriteEnvScript(app);
        }
    }

    // After every command, also a failed one, what it saved before failing is loaded by the shells too.
    private static void WriteEnvScript(IServiceProvider app)
    {
        try
        {
            app.GetRequiredService<IToolManager>().WriteEnvScript();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Could not write the script that consoles load, `dt shell env` writes it: {e.Message}");
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
        services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromMinutes(30) });
        services.AddSingleton<GitHubVersionSource>();
        services.AddSingleton<WebPageVersionSource>();
        services.AddSingleton<IVersionSource, VersionSourceRouter>();
        services.AddSingleton<IDownloader, HttpDownloader>();
        services.AddSingleton<VersionCache>();
        services.AddSingleton<IToolInstaller, ToolInstaller>();
        services.AddSingleton(provider => new Lazy<IToolInstaller>(provider.GetRequiredService<IToolInstaller>));
        services.AddSingleton<IRepositoryClient, RepositoryClient>();
        services.AddSingleton<ISourceManager, SourceManager>();
        services.AddSingleton(provider => new Lazy<ISourceManager>(provider.GetRequiredService<ISourceManager>));

        return services.BuildServiceProvider();
    }
}