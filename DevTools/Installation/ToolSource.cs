using System.Collections.Generic;
using System.Text.Json.Serialization;

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

public class VersionSource
{
    public string Type { get; set; }
    public string Repo { get; set; }
    public string Tag { get; set; }
    public string Url { get; set; }
    public string Pattern { get; set; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(DownloadStep), "download")]
[JsonDerivedType(typeof(ExtractStep), "extract")]
[JsonDerivedType(typeof(MoveStep), "move")]
public abstract class InstallStep
{
    // The step only runs on this system or architecture, e.g. "windows" or "arm64".
    public string Os { get; set; }
    public string Arch { get; set; }
}

public class DownloadStep : InstallStep
{
    public string Asset { get; set; }
    public string Url { get; set; }
    public string To { get; set; }
}

public class ExtractStep : InstallStep
{
    public string File { get; set; }
    public bool StripRoot { get; set; }
}

public class MoveStep : InstallStep
{
    public string From { get; set; }
    public string To { get; set; }
}