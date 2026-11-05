using System.Threading.Tasks;

namespace DevTools.App
{
    public interface IDownloader
    {
        Task DownloadAsync(string url, string fileName);
    }
}
