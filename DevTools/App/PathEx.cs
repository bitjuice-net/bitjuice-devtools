using System.IO;

namespace DevTools.App
{
    public class PathEx
    {
        public static string GetRootedPath(string path)
        {
            return GetRootedPath(path, Common.AssemblyDirectory);
        }

        public static string GetRootedPath(string path, string basePath)
        {
            return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(basePath, path));
        }
    }
}