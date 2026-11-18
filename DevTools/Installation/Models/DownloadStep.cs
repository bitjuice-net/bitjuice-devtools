namespace DevTools.Installation.Models;

public class DownloadStep : InstallStep
{
    public string Asset { get; set; }
    public string Url { get; set; }
    public string To { get; set; }
}
