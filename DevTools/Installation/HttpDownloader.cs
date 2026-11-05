using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace DevTools.Installation;

public class HttpDownloader : IDownloader
{
    private readonly HttpClient client;

    public HttpDownloader(HttpClient client)
    {
        this.client = client;
    }

    public async Task DownloadAsync(string url, string fileName)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("bitjuice-devtools");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using var file = File.Create(fileName);
        await response.Content.CopyToAsync(file);
    }
}