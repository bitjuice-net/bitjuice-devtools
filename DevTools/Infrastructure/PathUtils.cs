using System;
using System.IO;

namespace DevTools.Infrastructure;

public class PathUtils
{
    public static string AssemblyDirectory => Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty);

    public static string GetRootedPath(string path)
    {
        if (path is null)
            throw new ArgumentNullException(nameof(path));
        return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(AssemblyDirectory, path));
    }
}