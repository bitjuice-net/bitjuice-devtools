using System;
using System.CommandLine;
using System.Linq;
using DevTools.Catalog;
using DevTools.Management;
using DevTools.Sources;

namespace DevTools.Cli;

internal class CommandFactory(IToolManager manager, Lazy<ISourceManager> sources) : ICommandFactory
{
    public RootCommand CreateRootCommand()
    {
        var cmd = new RootCommand("Installs development tools and switches between their versions.");

        cmd.Subcommands.Add(BuildAppCommand());
        cmd.Subcommands.Add(BuildCacheCommand());
        cmd.Subcommands.Add(BuildSourceCommand());
        cmd.Subcommands.Add(BuildShellCommand());

        cmd.Subcommands.Add(Shortcut("ls", "app list", BuildListCommand()));
        cmd.Subcommands.Add(Shortcut("sl", "app select", BuildSelectCommand()));
        cmd.Subcommands.Add(Shortcut("sc", "cache scan", BuildScanCommand()));

        return cmd;
    }

    // A top-level command that takes the arguments, options and action of a grouped one.
    private static Command Shortcut(string name, string target, Command command)
    {
        var shortcut = new Command(name, $"Shortcut for `{target}`.") { Action = command.Action };
        foreach (var argument in command.Arguments)
            shortcut.Arguments.Add(argument);
        foreach (var option in command.Options)
            shortcut.Options.Add(option);
        return shortcut;
    }

    private Command BuildAppCommand()
    {
        var cmd = new Command("app", "Select, install, update and remove application versions.");
        cmd.Subcommands.Add(BuildListCommand());
        cmd.Subcommands.Add(BuildSelectCommand());
        cmd.Subcommands.Add(BuildEnableCommand());
        cmd.Subcommands.Add(BuildDisableCommand());
        cmd.Subcommands.Add(BuildInstallCommand());
        cmd.Subcommands.Add(BuildUpdateCommand());
        cmd.Subcommands.Add(BuildRemoveCommand());
        return cmd;
    }

    private Command BuildCacheCommand()
    {
        var cmd = new Command("cache", "The list of applications found in the tools directory.");
        cmd.Subcommands.Add(BuildScanCommand());
        cmd.Subcommands.Add(BuildPruneCommand());
        return cmd;
    }

    private Command BuildSourceCommand()
    {
        var cmd = new Command("source", "Repositories of .app and .source files used by `app install` and `app update`.");

        var addArgument = new Argument<string>("repository") { Description = "owner/repo on GitHub or a full directory path." };
        var add = new Command("add", "Add a manifest repository.") { addArgument };
        add.SetAction(result => sources.Value.Add(result.GetValue(addArgument)));
        cmd.Subcommands.Add(add);

        var removeArgument = new Argument<string>("repository");
        var remove = new Command("remove", "Remove a manifest repository, manifests fetched from it stay.") { removeArgument };
        remove.Aliases.Add("rm");
        remove.SetAction(result => sources.Value.Remove(result.GetValue(removeArgument)));
        cmd.Subcommands.Add(remove);

        var listArgument = new Argument<string>("repository") { Arity = ArgumentArity.ZeroOrOne, Description = "List the applications of this repository." };
        var list = new Command("list", "List manifest repositories.") { listArgument };
        list.Aliases.Add("ls");
        list.SetAction(result => sources.Value.List(result.GetValue(listArgument)));
        cmd.Subcommands.Add(list);

        var sync = new Command("sync", "Get the newest lists of applications from all sources.");
        sync.SetAction(_ => sources.Value.Sync());
        cmd.Subcommands.Add(sync);


        return cmd;
    }

    private Command BuildShellCommand()
    {
        var cmd = new Command("shell", "Shell integration, `shell install` gives every new shell the `dt` command.");

        cmd.Subcommands.Add(ShellSubcommand("install", "Load devtools in every new shell: AutoRun in the registry for cmd, ~/.bashrc or ~/.zshrc for the others.",
            shell => ShellInstaller.Install(shell, Environment.ProcessPath)));
        cmd.Subcommands.Add(ShellSubcommand("uninstall", "Stop loading devtools in new shells, other AutoRun commands and ~/.bashrc or ~/.zshrc lines stay.",
            ShellInstaller.Uninstall));
        cmd.Subcommands.Add(ShellSubcommand("hook", "Get the code that defines `dt` and loads the selected tools, `shell install` puts it in AutoRun or the rc file.",
            shell => Console.WriteLine(ShellScripts.Hook(shell, Environment.ProcessPath))));
        cmd.Subcommands.Add(ShellSubcommand("env", "Get commands that set PATH and environment variables of the selected tools, `dt` runs them after every command.",
            manager.GetEnv));

        return cmd;
    }

    private static Command ShellSubcommand(string name, string description, Action<Shell> action)
    {
        var shellArgument = new Argument<string>("shell")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = "auto is cmd on Windows and $SHELL elsewhere.",
            DefaultValueFactory = _ => AutoShell
        };
        shellArgument.AcceptOnlyFromAmong(Enum.GetNames<Shell>().Select(i => i.ToLowerInvariant()).Prepend(AutoShell).ToArray());
        var cmd = new Command(name, description) { shellArgument };
        cmd.SetAction(result => action(ParseShell(result.GetValue(shellArgument))));
        return cmd;
    }

    private const string AutoShell = "auto";

    private static Shell ParseShell(string value)
    {
        return value == AutoShell
            ? ShellScripts.Detect(OperatingSystem.IsWindows(), Environment.GetEnvironmentVariable)
            : Enum.Parse<Shell>(value, ignoreCase: true);
    }

    private Command BuildScanCommand()
    {
        var cmd = new Command("scan", "Find applications and versions in the tools directory.");
        cmd.SetAction(_ => manager.Scan());
        return cmd;
    }

    private Command BuildPruneCommand()
    {
        var cmd = new Command("prune", "Scan again and remove settings of applications and versions that no longer exist.");
        cmd.SetAction(_ => manager.Prune());
        return cmd;
    }

    private Command BuildInstallCommand()
    {
        var applicationArgument = new Argument<string>("application");
        var versionArgument = new Argument<string>("version")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = "Version to install, latest by default.",
            DefaultValueFactory = _ => MetaVersion.Latest
        };
        var cmd = new Command("install", "Download and install a version of an application.") { applicationArgument, versionArgument };
        cmd.SetAction(result => manager.Install(result.GetValue(applicationArgument), result.GetValue(versionArgument)));
        return cmd;
    }

    private Command BuildUpdateCommand()
    {
        var applicationArgument = new Argument<string>("application")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = "Without it every application with a .source is updated."
        };
        var cmd = new Command("update", "Install the newest version when it is newer than the installed ones.") { applicationArgument };
        cmd.SetAction(result =>
        {
            var application = result.GetValue(applicationArgument);
            if (application == null)
                manager.UpdateAll();
            else
                manager.Update(application);
        });
        return cmd;
    }

    private Command BuildRemoveCommand()
    {
        var applicationArgument = new Argument<string>("application");
        var versionArgument = new Argument<string>("version") { Description = "Exact version to delete from disk." };
        var cmd = new Command("remove", "Delete an installed version of an application.") { applicationArgument, versionArgument };
        cmd.Aliases.Add("rm");
        cmd.SetAction(result => manager.Remove(result.GetValue(applicationArgument), result.GetValue(versionArgument)));
        return cmd;
    }

    private Command BuildListCommand()
    {
        var applicationArgument = new Argument<string>("application") { Arity = ArgumentArity.ZeroOrOne, Description = "Show the details of this application." };
        var allOption = new Option<bool>("--all", "-a") { Description = "Include orphaned settings of applications that no longer exist." };
        var cmd = new Command("list", "List applications and their versions.") { applicationArgument, allOption };
        cmd.Aliases.Add("ls");
        cmd.SetAction(result =>
        {
            var application = result.GetValue(applicationArgument);
            if (application == null)
                manager.List(result.GetValue(allOption));
            else
                manager.Show(application);
        });
        return cmd;
    }

    private Command BuildSelectCommand()
    {
        var applicationArgument = new Argument<string>("application");
        var versionArgument = new Argument<string>("version") { Arity = ArgumentArity.ZeroOrOne };
        var cmd = new Command("select", "Select the version of an application, without a version choose it from a list.") { applicationArgument, versionArgument };
        cmd.SetAction(result => manager.Select(result.GetValue(applicationArgument), result.GetValue(versionArgument)));
        cmd.Aliases.Add("s");
        return cmd;
    }

    private Command BuildDisableCommand()
    {
        var applicationArgument = new Argument<string>("application");
        var cmd = new Command("disable", "Take an application off PATH without deleting it.") { applicationArgument };
        cmd.Aliases.Add("d");
        cmd.SetAction(result => manager.SetDisabled(result.GetValue(applicationArgument), true));

        return cmd;
    }

    private Command BuildEnableCommand()
    {
        var applicationArgument = new Argument<string>("application");
        var cmd = new Command("enable", "Put a disabled application back on PATH.") { applicationArgument };
        cmd.Aliases.Add("e");
        cmd.SetAction(result => manager.SetDisabled(result.GetValue(applicationArgument), false));
        return cmd;
    }
}