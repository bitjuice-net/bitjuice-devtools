using System;
using System.CommandLine;
using System.Linq;
using DevTools.Catalog;
using DevTools.Management;

namespace DevTools.Cli;

internal class CommandFactory(IToolManager manager) : ICommandFactory
{
    public RootCommand CreateRootCommand()
    {
        var cmd = new RootCommand("Manages list of development tool. Allows easy switching between different versions of the same app.");

        cmd.Subcommands.Add(BuildAppCommand());
        cmd.Subcommands.Add(BuildCacheCommand());
        cmd.Subcommands.Add(BuildShellCommand());

        cmd.Subcommands.Add(Shortcut("ls", "app list", BuildListCommand()));
        cmd.Subcommands.Add(Shortcut("s", "app select", BuildSelectCommand()));
        cmd.Subcommands.Add(Shortcut("e", "app enable", BuildEnableCommand()));
        cmd.Subcommands.Add(Shortcut("d", "app disable", BuildDisableCommand()));
        cmd.Subcommands.Add(Shortcut("i", "app install", BuildInstallCommand()));
        cmd.Subcommands.Add(Shortcut("u", "app update", BuildUpdateCommand()));
        cmd.Subcommands.Add(Shortcut("rm", "app remove", BuildRemoveCommand()));
        cmd.Subcommands.Add(Shortcut("discover", "cache discover", BuildDiscoverCommand()));

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
        var cmd = new Command("app", "Applications: choose versions, download, update and delete them.");
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
        var cmd = new Command("cache", "Application cache built by scanning the tools directory.");
        cmd.Subcommands.Add(BuildDiscoverCommand());
        cmd.Subcommands.Add(BuildPruneCommand());
        return cmd;
    }

    private Command BuildShellCommand()
    {
        var cmd = new Command("shell", "Shell integration: cmd, bash or zsh, the default is cmd on Windows and bash elsewhere.");
        cmd.Subcommands.Add(BuildPathCommand());
        cmd.Subcommands.Add(BuildEnvsCommand());

        cmd.Subcommands.Add(ShellSubcommand("init", "Get the script that defines `dt` and loads the selected tools, for cmd it is the AutoRun command.",
            shell => Console.WriteLine(ShellScripts.Init(shell, Environment.ProcessPath))));
        cmd.Subcommands.Add(ShellSubcommand("setup", "Get commands that set PATH and environment variables of the selected tools.",
            manager.GetSetup));
        cmd.Subcommands.Add(ShellSubcommand("install", "Load devtools in every new shell: AutoRun in the registry for cmd, ~/.bashrc or ~/.zshrc for the others.",
            shell => ShellInstaller.Install(shell, Environment.ProcessPath)));
        cmd.Subcommands.Add(ShellSubcommand("uninstall", "Stop loading devtools in new shells, other AutoRun commands and ~/.bashrc or ~/.zshrc lines stay.",
            ShellInstaller.Uninstall));

        return cmd;
    }

    private static Command ShellSubcommand(string name, string description, Action<Shell> action)
    {
        var shellArgument = new Argument<string>("shell")
        {
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => ShellScripts.Default.ToString().ToLowerInvariant()
        };
        shellArgument.AcceptOnlyFromAmong(Enum.GetNames<Shell>().Select(i => i.ToLowerInvariant()).ToArray());
        var cmd = new Command(name, description) { shellArgument };
        cmd.SetAction(result => action(Enum.Parse<Shell>(result.GetValue(shellArgument), ignoreCase: true)));
        return cmd;
    }

    private Command BuildPathCommand()
    {
        var cmd = new Command("path", "Get the PATH entries of the selected tools.");
        cmd.SetAction(_ => manager.GetPath());
        return cmd;
    }

    private Command BuildEnvsCommand()
    {
        var cmd = new Command("envs", "Get the environment variables of the selected tools.");
        cmd.SetAction(_ => manager.GetEnvs());
        return cmd;
    }

    private Command BuildDiscoverCommand()
    {
        var cmd = new Command("discover", "Scan the tools directory for applications and versions.");
        cmd.SetAction(_ => manager.Discover());
        return cmd;
    }

    private Command BuildPruneCommand()
    {
        var cmd = new Command("prune", "Refresh the application cache and remove settings of applications and versions that no longer exist.");
        cmd.SetAction(_ => manager.Prune());
        return cmd;
    }

    private Command BuildInstallCommand()
    {
        var applicationArgument = new Argument<string>("application");
        var versionArgument = new Argument<string>("version")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = "Version to install or latest.",
            DefaultValueFactory = _ => MetaVersion.Latest
        };
        var cmd = new Command("install", "Download and install application version.") { applicationArgument, versionArgument };
        cmd.SetAction(result => manager.Install(result.GetValue(applicationArgument), result.GetValue(versionArgument)));
        return cmd;
    }

    private Command BuildUpdateCommand()
    {
        var applicationArgument = new Argument<string>("application");
        var cmd = new Command("update", "Install the newest version if it is newer than the installed ones.") { applicationArgument };
        cmd.SetAction(result => manager.Update(result.GetValue(applicationArgument)));
        return cmd;
    }

    private Command BuildRemoveCommand()
    {
        var applicationArgument = new Argument<string>("application");
        var versionArgument = new Argument<string>("version") { Description = "Exact version to delete from disk." };
        var cmd = new Command("remove", "Delete an installed version of a versioned application.") { applicationArgument, versionArgument };
        cmd.Aliases.Add("rm");
        cmd.SetAction(result => manager.Remove(result.GetValue(applicationArgument), result.GetValue(versionArgument)));
        return cmd;
    }

    private Command BuildListCommand()
    {
        var allOption = new Option<bool>("--all", "-a") { Description = "Include orphaned settings of applications that no longer exist." };
        var cmd = new Command("list", "List available applications.") { allOption };
        cmd.Aliases.Add("ls");
        cmd.SetAction(result => manager.List(result.GetValue(allOption)));
        return cmd;
    }

    private Command BuildSelectCommand()
    {
        var applicationArgument = new Argument<string>("application");
        var versionArgument = new Argument<string>("version") { Arity = ArgumentArity.ZeroOrOne };
        var cmd = new Command("select", "Select application version.") { applicationArgument, versionArgument };
        cmd.SetAction(result => manager.Select(result.GetValue(applicationArgument), result.GetValue(versionArgument)));
        cmd.Aliases.Add("s");
        return cmd;
    }

    private Command BuildDisableCommand()
    {
        var applicationArgument = new Argument<string>("application");
        var cmd = new Command("disable", "Disable application.") { applicationArgument };
        cmd.Aliases.Add("d");
        cmd.SetAction(result => manager.SetDisabled(result.GetValue(applicationArgument), true));

        return cmd;
    }

    private Command BuildEnableCommand()
    {
        var applicationArgument = new Argument<string>("application");
        var cmd = new Command("enable", "Enable application.") { applicationArgument };
        cmd.Aliases.Add("e");
        cmd.SetAction(result => manager.SetDisabled(result.GetValue(applicationArgument), false));
        return cmd;
    }
}