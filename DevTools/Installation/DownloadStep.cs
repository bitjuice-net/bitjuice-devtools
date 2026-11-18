namespace DevTools.Installation;

public class DownloadStep : InstallStep
{
    public string Asset { get; set; }
    public string Url { get; set; }
    public string To { get; set; }
}
