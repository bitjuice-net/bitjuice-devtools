using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DevTools.Installation
{
    public class GitHubVersionSource : IVersionSource
    {
        private const string Releases = "github-releases";
        private const string Tags = "github-tags";
        private const string DefaultTagPattern = @"^v?(?<version>\d+\.\d+\.\d+)$";
        private const int PageSize = 100;

        private static readonly Regex RepoPattern = new(@"^[\w.-]+/[\w.-]+$");

        private readonly HttpClient client;

        public GitHubVersionSource(HttpClient client)
        {
            this.client = client;
        }

        public async Task<List<RemoteVersion>> GetVersionsAsync(VersionSource source)
        {
            if (source.Type is not (Releases or Tags))
                throw new InvalidOperationException($"Unsupported version source type `{source.Type}`");

            if (source.Repo == null || !RepoPattern.IsMatch(source.Repo))
                throw new InvalidOperationException($"Invalid GitHub repository `{source.Repo}`");

            var tagPattern = new Regex(source.Tag ?? DefaultTagPattern, RegexOptions.None, TimeSpan.FromSeconds(1));
            var resource = source.Type == Releases ? "releases" : "tags";
            var versions = new List<RemoteVersion>();

            for (var page = 1; ; page++)
            {
                using var document = await GetPageAsync($"https://api.github.com/repos/{source.Repo}/{resource}?per_page={PageSize}&page={page}", source.Repo, page);

                if (document == null)
                {
                    Console.Error.WriteLine($"Warning: GitHub lists only the newest {(page - 1) * PageSize} {resource} of {source.Repo}, older versions are not available");
                    break;
                }

                foreach (var item in document.RootElement.EnumerateArray())
                {
                    var version = source.Type == Releases ? ParseRelease(item, tagPattern) : ParseTag(item, tagPattern);
                    if (version != null)
                        versions.Add(version);
                }

                if (document.RootElement.GetArrayLength() < PageSize)
                    break;
            }

            return versions
                .GroupBy(i => i.Version)
                .Select(i => i.First())
                .OrderByDescending(i => Version.Parse(i.Version))
                .ToList();
        }

        private async Task<JsonDocument> GetPageAsync(string url, string repo, int page)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.UserAgent.ParseAdd("bitjuice-devtools");

            var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await client.SendAsync(request);

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                throw new InvalidOperationException($"GitHub refused the request for {repo} ({(int)response.StatusCode}). The API rate limit may be exceeded, set GITHUB_TOKEN to raise it.");

            if (response.StatusCode == HttpStatusCode.UnprocessableEntity && page > 1)
                return null;

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"GitHub returned {(int)response.StatusCode} for {repo}");

            return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        }

        private static RemoteVersion ParseRelease(JsonElement release, Regex tagPattern)
        {
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())
                return null;

            var assets = release.GetProperty("assets")
                .EnumerateArray()
                .Select(i => new RemoteAsset(
                    i.GetProperty("name").GetString(),
                    i.GetProperty("browser_download_url").GetString(),
                    i.TryGetProperty("digest", out var digest) ? digest.GetString() : null))
                .ToList();

            return CreateVersion(release.GetProperty("tag_name").GetString(), tagPattern, assets);
        }

        private static RemoteVersion ParseTag(JsonElement tag, Regex tagPattern)
        {
            return CreateVersion(tag.GetProperty("name").GetString(), tagPattern, new List<RemoteAsset>());
        }

        private static RemoteVersion CreateVersion(string tag, Regex tagPattern, List<RemoteAsset> assets)
        {
            var match = tagPattern.Match(tag);
            var version = match.Groups["version"].Value;

            return match.Success && Version.TryParse(version, out _) ? new RemoteVersion(version, tag, assets) : null;
        }
    }
}
