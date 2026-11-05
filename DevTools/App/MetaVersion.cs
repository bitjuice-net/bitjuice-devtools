namespace DevTools.App
{
    public static class MetaVersion
    {
        public const string Latest = "latest";
        public const string Oldest = "oldest";

        // Version of an application that is not versioned. It is an exact name and it is never written in a manifest.
        public const string Default = "default";

        public static bool IsMeta(string version)
        {
            return version is Latest or Oldest;
        }
    }
}
