using System;
using System.IO;

namespace DevTools.Infrastructure;

public class PathUtils
{
    public static string AssemblyDirectory => Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty);

    // Settings and choices of the user, they cannot be rebuilt.
    public const string ConfigDirectoryName = "config";

    // Everything devtools can rebuild, any file may be deleted.
    public const string CacheDirectoryName = "cache";

    public const string StateFileName = "state";

    public static string GetConfigPath(string fileName)
    {
        return Path.Combine(AssemblyDirectory, ConfigDirectoryName, fileName);
    }

    public static string GetCachePath(string fileName)
    {
        return Path.Combine(AssemblyDirectory, CacheDirectoryName, fileName);
    }

    // Changes whenever something shells load changes, so `dt` reloads them only then.
    public static string StateFile => GetCachePath(StateFileName);

    public static string GetRootedPath(string path)
    {
        if (path is null)
            throw new ArgumentNullException(nameof(path));
        return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(AssemblyDirectory, path));
    }
}