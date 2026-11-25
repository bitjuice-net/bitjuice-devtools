using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using DevTools.Infrastructure;
using Microsoft.Extensions.Options;

namespace DevTools.Sources;

// A repository is owner/repo on GitHub, read from its default branch, or a local directory, handy while writing manifests.
// Files come from raw.githubusercontent.com, which has no rate limit. Only listing a directory needs the GitHub API.
public class RepositoryClient(HttpClient client, IOptions<Settings> options = null) : IRepositoryClient
{
    public async Task<byte[]> GetFileAsync(string repository, string path)
    {
        if (Path.IsPathRooted(repository))
        {
            var file = Path.Combine(repository, path);
            return File.Exists(file) ? await File.ReadAllBytesAsync(file) : null;
        }

        using var response = await SendAsync($"https://raw.githubusercontent.com/{repository}/HEAD/{path}");
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
    }

    public async Task<List<string>> ListFilesAsync(string repository, string directory)
    {
        if (Path.IsPathRooted(repository))
        {
            var path = Path.Combine(repository, directory);
            return Directory.Exists(path) ? Directory.GetFiles(path).Select(Path.GetFileName).ToList() : null;
        }

        using var response = await SendAsync($"https://api.github.com/repos/{repository}/contents/{directory}", "application/vnd.github+json");
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException($"GitHub API refused to list {repository}, it allows 60 requests an hour without a token, set githubToken in config/settings.json or {Settings.GitHubTokenVariable}");

        response.EnsureSuccessStatusCode();

        // An array of entries, files and directories.
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return document.RootElement.EnumerateArray()
            .Where(i => i.GetProperty("type").GetString() == "file")
            .Select(i => i.GetProperty("name").GetString())
            .ToList();
    }

    private async Task<HttpResponseMessage> SendAsync(string url, string accept = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
        request.Headers.UserAgent.ParseAdd("bitjuice-devtools");
        if (accept != null)
            request.Headers.Accept.ParseAdd(accept);

        // A private repository needs the token, raw.githubusercontent.com is GitHub too.
        var token = (options?.Value ?? new Settings()).ResolveGitHubToken();
        if (token != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await client.SendAsync(request);
    }
}
