using System.Threading.Tasks;

namespace DevTools.Installation
{
    public interface IDownloader
    {
        Task DownloadAsync(string url, string fileName);
    }
}
