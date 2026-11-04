using System.CommandLine;

namespace DevTools.App;

internal class CommandFactory : ICommandFactory
{
    public readonly IToolManager manager;

    public CommandFactory(IToolManager manager)
    {
        this.manager = manager;
    }

    public RootCommand CreateRootCommand()
    {
        var cmd = new RootCommand("Manages list of development tool. Allows easy switching between different versions of the same app.");

        cmd.Subcommands.Add(BuildDiscoverCommand());
        cmd.Subcommands.Add(BuildSetupCommand());
        cmd.Subcommands.Add(BuildPathCommand());
        cmd.Subcommands.Add(BuildEnvsCommand());
        cmd.Subcommands.Add(BuildListCommand());
        cmd.Subcommands.Add(BuildSelectCommand());
        cmd.Subcommands.Add(BuildDisableCommand());
        cmd.Subcommands.Add(BuildEnableCommand());

        return cmd;
    }

    private Command BuildSetupCommand()
    {
        var cmd = new Command("setup", "Get setup string.");
        cmd.SetAction(_ => manager.GetSetup());
        return cmd;
    }

    private Command BuildPathCommand()
    {
        var cmd = new Command("path", "Get PATH string.");
        cmd.SetAction(_ => manager.GetPath());
        return cmd;
    }

    private Command BuildEnvsCommand()
    {
        var cmd = new Command("envs", "Get environment variables.");
        cmd.SetAction(_ => manager.GetEnvs());
        return cmd;
    }

    private Command BuildDiscoverCommand()
    {
        var cmd = new Command("discover", "Discover applications.");
        cmd.SetAction(_ => manager.Discover());
        return cmd;
    }

    private Command BuildListCommand()
    {
        var cmd = new Command("list", "List available applications.");
        cmd.Aliases.Add("ls");
        cmd.SetAction(_ => manager.List());
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