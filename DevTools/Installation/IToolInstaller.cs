using System.Collections.Generic;
using System.Threading.Tasks;
using DevTools.Installation.Models;

namespace DevTools.Installation;

public interface IToolInstaller
{
    // From the version cache unless refresh is set or the cached versions are older than an hour.
    Task<List<RemoteVersion>> GetVersionsAsync(string directory, bool refresh = false);
    Task<string> InstallAsync(string directory, RemoteVersion version);
    void Remove(string versionDirectory);
}