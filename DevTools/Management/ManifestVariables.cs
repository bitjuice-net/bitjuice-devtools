using System;
using System.Text.RegularExpressions;

namespace DevTools.Management;

// %VAR% in paths and envs of .app files. Manifests are shared between systems, so they mean the same everywhere:
// %APPDIR% is the application directory, %HOME% and %USERPROFILE% the home of the user, other names come from the environment.
public static partial class ManifestVariables
{
    public static string Expand(string value, string appDir)
    {
        // Manifests are usually written with \, which is not a separator outside of Windows.
        if (!OperatingSystem.IsWindows())
            value = value.Replace('\\', '/');

        return Variable().Replace(value, match => Resolve(match.Groups[1].Value, appDir) ?? match.Value);
    }

    private static string Resolve(string name, string appDir)
    {
        if (name.Equals("APPDIR", StringComparison.OrdinalIgnoreCase))
            return appDir;

        if (name.Equals("HOME", StringComparison.OrdinalIgnoreCase) || name.Equals("USERPROFILE", StringComparison.OrdinalIgnoreCase))
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return Environment.GetEnvironmentVariable(name);
    }

    [GeneratedRegex("%([^%]+)%")]
    private static partial Regex Variable();
}
