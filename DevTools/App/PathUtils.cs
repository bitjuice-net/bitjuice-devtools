using System;
using System.Diagnostics;
using System.IO;

namespace DevTools.App
{
    public class PathUtils
    {
        public static string AssemblyDirectory => Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty);

        public static string GetRootedPath(string path)
        {
            if (path is null)
                throw new ArgumentNullException(nameof(path));
            return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(AssemblyDirectory, path));
        }
    }
}