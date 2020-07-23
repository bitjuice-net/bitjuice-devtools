using System.Collections.Generic;

namespace DevTools.App
{
    public static class StringExtensions
    {
        public static string Join(this IEnumerable<string> strings, char separator)
        {
            return string.Join(separator, strings);
        }

        public static string Join(this IEnumerable<string> strings, string separator)
        {
            return string.Join(separator, strings);
        }
    }
}
