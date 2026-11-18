namespace DevTools.Installation;

public class ExtractStep : InstallStep
{
    public string File { get; set; }
    public bool StripRoot { get; set; }
}
