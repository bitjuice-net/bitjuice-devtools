using System.Collections.Generic;
using System.Threading.Tasks;
using DevTools.Installation.Models;

namespace DevTools.Installation;

public class VersionSourceRouter(GitHubVersionSource gitHub, WebPageVersionSource webPage) : IVersionSource
{
    public Task<List<RemoteVersion>> GetVersionsAsync(VersionSource source)
    {
        return source.Type == WebPageVersionSource.TypeName
            ? webPage.GetVersionsAsync(source)
            : gitHub.GetVersionsAsync(source);
    }
}
