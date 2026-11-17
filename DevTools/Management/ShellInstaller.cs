using System;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace DevTools.Management;

public static class ShellInstaller
{
    private const string CommandProcessorKey = @"Software\Microsoft\Command Processor";
    private const string AutoRunValue = "AutoRun";

    public static void Install(Shell shell, string executable)
    {
        if (shell != Shell.Cmd)
            InstallRc(shell, executable);
        else if (OperatingSystem.IsWindows())
            InstallCmd(executable);
        else
            throw CmdNotSupported();
    }

    public static void Uninstall(Shell shell)
    {
        if (shell != Shell.Cmd)
            UninstallRc(shell);
        else if (OperatingSystem.IsWindows())
            UninstallCmd();
        else
            throw CmdNotSupported();
    }

    [SupportedOSPlatform("windows")]
    private static void InstallCmd(string executable)
    {
        using var key = OpenCommandProcessor();
        var existing = key.GetValue(AutoRunValue) as string;
        var command = ShellScripts.MergeAutoRun(existing, ShellScripts.Hook(Shell.Cmd, executable));

        key.SetValue(AutoRunValue, command, RegistryValueKind.String);
        if (!string.IsNullOrWhiteSpace(existing) && existing != command)
            Console.WriteLine($"Previous AutoRun: {existing}");
        Console.WriteLine($@"Set HKCU\{CommandProcessorKey}\{AutoRunValue}, open a new console to use it");
    }

    [SupportedOSPlatform("windows")]
    private static void UninstallCmd()
    {
        using var key = OpenCommandProcessor();
        var existing = key.GetValue(AutoRunValue) as string;
        var others = ShellScripts.RemoveFromAutoRun(existing);

        if (string.IsNullOrWhiteSpace(existing) || others == existing.Trim())
        {
            Console.WriteLine("AutoRun does not load devtools");
            return;
        }

        if (others.Length == 0)
            key.DeleteValue(AutoRunValue);
        else
            key.SetValue(AutoRunValue, others, RegistryValueKind.String);

        Console.WriteLine($"Removed devtools from AutoRun, open a new console to use it. Previous AutoRun: {existing}");
    }

    [SupportedOSPlatform("windows")]
    private static RegistryKey OpenCommandProcessor()
    {
        return Registry.CurrentUser.CreateSubKey(CommandProcessorKey);
    }

    private static InvalidOperationException CmdNotSupported()
    {
        return new InvalidOperationException("cmd integration is only available on Windows");
    }

    private static void InstallRc(Shell shell, string executable)
    {
        var file = ShellScripts.RcFile(shell, Environment.GetEnvironmentVariable);
        var line = ShellScripts.RcLine(shell, executable);

        if (File.Exists(file) && File.ReadAllText(file).Contains(line))
        {
            Console.WriteLine($"{file} already loads devtools");
            return;
        }

        File.AppendAllText(file, $"\n{ShellScripts.RcMarker}\n{line}\n");
        Console.WriteLine($"Added devtools to {file}, open a new shell or run: {line}");
    }

    private static void UninstallRc(Shell shell)
    {
        var file = ShellScripts.RcFile(shell, Environment.GetEnvironmentVariable);
        var content = File.Exists(file) ? File.ReadAllText(file) : "";
        var cleaned = ShellScripts.RemoveFromRc(content, shell);

        if (cleaned == content)
        {
            Console.WriteLine($"{file} does not load devtools");
            return;
        }

        File.WriteAllText(file, cleaned);
        Console.WriteLine($"Removed devtools from {file}, open a new shell to use it");
    }
}
