using System.Diagnostics;
using System.IO;

namespace DevTools
{
    public class Common
    {
        public static string AssemblyDirectory => Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty);
    }
}