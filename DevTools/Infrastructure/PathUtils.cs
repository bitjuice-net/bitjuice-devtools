using System;
using System.IO;

namespace DevTools.Infrastructure;

public class PathUtils
{
    public static string AssemblyDirectory => Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty);

    // Settings and state of devtools, apart from the program itself.
    public const string DataDirectoryName = "data";

    public static string GetDataPath(string fileName)
    {
        return Path.Combine(AssemblyDirectory, DataDirectoryName, fileName);
    }

    public static string GetRootedPath(string path)
    {
        if (path is null)
            throw new ArgumentNullException(nameof(path));
        return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(AssemblyDirectory, path));
    }
}