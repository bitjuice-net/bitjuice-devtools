using System.Collections.Generic;
using System.Threading.Tasks;
using DevTools.Installation.Models;

namespace DevTools.Installation;

public interface IVersionSource
{
    Task<List<RemoteVersion>> GetVersionsAsync(VersionSource source);
}