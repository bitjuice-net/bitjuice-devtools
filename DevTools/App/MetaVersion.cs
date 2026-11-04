namespace DevTools.App
{
    public static class MetaVersion
    {
        public const string Latest = "latest";
        public const string Oldest = "oldest";

        public static bool IsMeta(string version)
        {
            return version is Latest or Oldest;
        }
    }
}
