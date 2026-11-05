using System.Collections.Generic;
using System.Threading.Tasks;

namespace DevTools.App
{
    public record RemoteAsset(string Name, string Url, string Digest);

    public record RemoteVersion(string Version, string Tag, IReadOnlyList<RemoteAsset> Assets);

    public interface IVersionSource
    {
        Task<List<RemoteVersion>> GetVersionsAsync(VersionSource source);
    }
}
