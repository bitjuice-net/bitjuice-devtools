using System;

namespace DevTools.Infrastructure;

public static class VersionNumber
{
    // 12.2 and 12.2.0 are one version. Anything that is not a two part number, like latest, stays as it is.
    public static string Normalize(string version)
    {
        return Version.TryParse(version, out var parsed) && parsed.Build < 0 ? $"{version}.0" : version;
    }

    public static bool AreEqual(string first, string second)
    {
        return Normalize(first) == Normalize(second);
    }

    public static bool TryParse(string version, out Version parsed)
    {
        return Version.TryParse(Normalize(version), out parsed);
    }
}
