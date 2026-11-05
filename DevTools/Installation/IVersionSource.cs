using System.Collections.Generic;
using System.Threading.Tasks;

namespace DevTools.Installation;

public interface IVersionSource
{
    Task<List<RemoteVersion>> GetVersionsAsync(VersionSource source);
}