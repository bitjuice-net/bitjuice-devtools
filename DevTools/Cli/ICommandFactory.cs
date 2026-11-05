using System.CommandLine;

namespace DevTools.Cli;

public interface ICommandFactory
{
    RootCommand CreateRootCommand();
}