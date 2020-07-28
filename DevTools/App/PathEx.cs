using System.IO;

namespace DevTools.App
{
    public class PathEx
    {
        public static string GetRootedPath(string path)
        {
            return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(Common.AssemblyDirectory, path));
        }
    }
}