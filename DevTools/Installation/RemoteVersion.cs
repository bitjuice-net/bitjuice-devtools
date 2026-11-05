using System.Collections.Generic;

namespace DevTools.Installation;

public record RemoteAsset(string Name, string Url, string Digest);

public record RemoteVersion(string Version, string Tag, IReadOnlyList<RemoteAsset> Assets);