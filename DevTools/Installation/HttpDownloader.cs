using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace DevTools.Installation;

public class HttpDownloader(HttpClient client) : IDownloader
{
    public async Task DownloadAsync(string url, string fileName)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("bitjuice-devtools");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using var input = await response.Content.ReadAsStreamAsync();
        await using var file = File.Create(fileName);

        // A redirected output gets no progress, it would only fill a log with lines.
        var progress = Console.IsOutputRedirected ? null : new DownloadProgress(response.Content.Headers.ContentLength, Console.Out);
        var buffer = new byte[81920];
        long done = 0;
        int read;

        while ((read = await input.ReadAsync(buffer)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read));
            done += read;
            progress?.Report(done, false);
        }

        progress?.Report(done, true);
    }
}
