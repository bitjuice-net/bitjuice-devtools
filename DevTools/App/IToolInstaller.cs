using System.Collections.Generic;
using System.Threading.Tasks;

namespace DevTools.App
{
    public interface IToolInstaller
    {
        Task<List<RemoteVersion>> GetVersionsAsync(string directory);
        Task<string> InstallAsync(string directory, RemoteVersion version);
        void Remove(string versionDirectory);
    }
}
