using System.Collections.Generic;

namespace DevTools.Installation;

// Version names the installed directory. SourceVersion is the number as the source writes it, for the {version} placeholder.
public record RemoteVersion(string Version, string Tag, IReadOnlyList<RemoteAsset> Assets, string SourceVersion = null);
