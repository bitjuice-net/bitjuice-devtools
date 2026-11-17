using System;
using System.Diagnostics;
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
        var progress = Console.IsOutputRedirected ? null : new Progress(response.Content.Headers.ContentLength);
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

    // One line rewritten in place, a few times a second.
    private class Progress(long? total)
    {
        private readonly Stopwatch watch = Stopwatch.StartNew();
        private TimeSpan last = TimeSpan.MinValue;
        private int width;

        public void Report(long done, bool finished)
        {
            if (!finished && watch.Elapsed - last < TimeSpan.FromMilliseconds(250))
                return;

            last = watch.Elapsed;
            var text = total is > 0
                ? $"  {Megabytes(done)} / {Megabytes(total.Value)} MB ({done * 100 / total.Value}%)"
                : $"  {Megabytes(done)} MB";

            Console.Write("\r" + text.PadRight(width));
            width = text.Length;

            if (finished)
                Console.WriteLine();
        }

        private static string Megabytes(long bytes)
        {
            return (bytes / 1024d / 1024d).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
