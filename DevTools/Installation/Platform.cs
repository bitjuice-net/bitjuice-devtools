using System;
using System.Runtime.InteropServices;

namespace DevTools.Installation;

// The names install sections of .app use for {os} and {arch} and in the os and arch of a step.
public record Platform(string Os, string Arch)
{
    public static Platform Current { get; } = new(CurrentOs(), RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant());

    private static string CurrentOs()
    {
        if (OperatingSystem.IsWindows())
            return "windows";
        if (OperatingSystem.IsLinux())
            return "linux";
        if (OperatingSystem.IsMacOS())
            return "macos";
        return RuntimeInformation.OSDescription.ToLowerInvariant();
    }
}
