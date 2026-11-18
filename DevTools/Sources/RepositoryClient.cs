using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using DevTools.Infrastructure;
using Microsoft.Extensions.Options;

namespace DevTools.Sources;

// A repository is owner/repo on GitHub, read from its default branch, or a local directory, handy while writing manifests.
public class RepositoryClient(HttpClient client, IOptions<Settings> options = null) : IRepositoryClient
{
    public async Task<byte[]> GetFileAsync(string repository, string path)
    {
        if (Path.IsPathRooted(repository))
        {
            var file = Path.Combine(repository, path);
            return File.Exists(file) ? await File.ReadAllBytesAsync(file) : null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"https://raw.githubusercontent.com/{repository}/HEAD/{path}"));
        request.Headers.UserAgent.ParseAdd("bitjuice-devtools");

        // A private repository needs the token, raw.githubusercontent.com is GitHub too.
        var token = (options?.Value ?? new Settings()).ResolveGitHubToken();
        if (token != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
    }
}
