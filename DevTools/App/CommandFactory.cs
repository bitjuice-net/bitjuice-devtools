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

        cmd.AddCommand(BuildDiscoverCommand());
        cmd.AddCommand(BuildSetupCommand());
        cmd.AddCommand(BuildPathCommand());
        cmd.AddCommand(BuildEnvsCommand());
        cmd.AddCommand(BuildListCommand());
        cmd.AddCommand(BuildSelectCommand());
        cmd.AddCommand(BuildDisableCommand());
        cmd.AddCommand(BuildEnableCommand());

        return cmd;
    }

    private Command BuildSetupCommand()
    {
        var cmd = new Command("setup", "Get setup string.");
        cmd.SetHandler(manager.GetSetup);
        return cmd;
    }

    private Command BuildPathCommand()
    {
        var cmd = new Command("path", "Get PATH string.");
        cmd.SetHandler(manager.GetPath);
        return cmd;
    }

    private Command BuildEnvsCommand()
    {
        var cmd = new Command("envs", "Get environment variables.");
        cmd.SetHandler(manager.GetEnvs);
        return cmd;
    }

    private Command BuildDiscoverCommand()
    {
        var cmd = new Command("discover", "Discover applications.");
        cmd.SetHandler(manager.Discover);
        return cmd;
    }

    private Command BuildListCommand()
    {
        var cmd = new Command("list", "List available applications.");
        cmd.AddAlias("ls");
        cmd.SetHandler(manager.List);
        return cmd;
    }

    private Command BuildSelectCommand()
    {
        var applicationArgument = new Argument<string>("application");
        var versionArgument = new Argument<string>("version") { Arity = ArgumentArity.ZeroOrOne };
        var cmd = new Command("select", "Select application version.") { applicationArgument, versionArgument };
        cmd.SetHandler(manager.Select, applicationArgument, versionArgument); ;
        cmd.AddAlias("s");
        return cmd;
    }

    private Command BuildDisableCommand()
    {
        var applicationArgument = new Argument<string>("application");
        var cmd = new Command("disable", "Disable application.") { applicationArgument };
        cmd.AddAlias("d");
        cmd.SetHandler(application => manager.SetDisabled(application, true), applicationArgument);

        return cmd;
    }

    private Command BuildEnableCommand()
    {
        var applicationArgument = new Argument<string>("application");
        var cmd = new Command("enable", "Enable application.") { applicationArgument };
        cmd.AddAlias("e");
        cmd.SetHandler(application => manager.SetDisabled(application, false), applicationArgument);
        return cmd;
    }
}