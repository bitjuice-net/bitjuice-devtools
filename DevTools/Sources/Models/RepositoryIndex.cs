namespace DevTools.Sources.Models;

// index.json in the root of a manifest repository, it marks the repository as one:
// { "name": "bitjuice devtools apps", "maintainer": "bitjuice-net" }. The applications are the files in apps.
public class RepositoryIndex
{
    public const string FileName = "index.json";

    // A friendly name shown by devtools.
    public string Name { get; set; }

    public string Maintainer { get; set; }
}
