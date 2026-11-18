using System.Collections.Generic;

namespace DevTools.Installation;

public class ToolSource
{
    public const string FileName = ".source";

    // Bumped in a manifest repository whenever .app or .source of the application changes.
    public int Revision { get; set; }

    public VersionSource Versions { get; set; }

    // Names used by the project in place of the devtools ones for {os} and {arch}, e.g. "x64": "amd64".
    public Dictionary<string, string> OsNames { get; set; }
    public Dictionary<string, string> ArchNames { get; set; }

    public List<InstallStep> Steps { get; set; }
}
