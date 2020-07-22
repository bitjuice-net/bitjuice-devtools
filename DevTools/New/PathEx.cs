using System.IO;

namespace DevTools.New
{
    public class PathEx
    {
        public static string GetRootedPath(string path, string basePath)
        {
            return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(basePath, path));
        }
    }
}