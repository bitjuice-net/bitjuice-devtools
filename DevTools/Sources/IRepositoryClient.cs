using System.Threading.Tasks;

namespace DevTools.Sources;

public interface IRepositoryClient
{
    // The file content, or null when the repository has no such file.
    Task<byte[]> GetFileAsync(string repository, string path);
}
