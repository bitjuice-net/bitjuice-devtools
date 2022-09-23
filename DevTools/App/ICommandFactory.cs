using System.CommandLine;

namespace DevTools.App;

public interface ICommandFactory
{
    RootCommand CreateRootCommand();
}