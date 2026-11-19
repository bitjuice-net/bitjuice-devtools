namespace DevTools.Catalog.Models;

// Written into .app when it is fetched from a manifest repository, so the tools directory alone knows where it came from.
public class ManifestOrigin
{
    public string Repository { get; set; }

    // Of the content without origin, a different one means the file was changed by hand.
    public string Sha256 { get; set; }
}
