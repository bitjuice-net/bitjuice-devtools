using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DevTools.Installation
{
    public class ToolSource
    {
        public const string FileName = ".source";

        public VersionSource Versions { get; set; }
        public List<InstallStep> Steps { get; set; }
    }

    public class VersionSource
    {
        public string Type { get; set; }
        public string Repo { get; set; }
        public string Tag { get; set; }
    }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [JsonDerivedType(typeof(DownloadStep), "download")]
    [JsonDerivedType(typeof(ExtractStep), "extract")]
    [JsonDerivedType(typeof(MoveStep), "move")]
    public abstract class InstallStep
    {
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
}
