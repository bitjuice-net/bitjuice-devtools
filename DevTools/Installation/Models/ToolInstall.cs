using System.Collections.Generic;

namespace DevTools.Installation.Models;

// The install section of .app: where the versions come from and how a version is installed.
public class ToolInstall
{
    public VersionSource Versions { get; set; }

    // Names used by the project in place of the devtools ones for {os} and {arch}, e.g. "x64": "amd64".
    public Dictionary<string, string> OsNames { get; set; }
    public Dictionary<string, string> ArchNames { get; set; }

    public List<InstallStep> Steps { get; set; }
}
