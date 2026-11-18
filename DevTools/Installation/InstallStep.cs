using System.Text.Json.Serialization;

namespace DevTools.Installation;

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
