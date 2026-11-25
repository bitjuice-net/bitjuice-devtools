using System.Collections.Generic;
using System.Threading.Tasks;

namespace DevTools.Sources;

public interface IRepositoryClient
{
    // The file content, or null when the repository has no such file.
    Task<byte[]> GetFileAsync(string repository, string path);

    // The names of the files in a directory of the repository, or null when there is no such directory.
    Task<List<string>> ListFilesAsync(string repository, string directory);
}
