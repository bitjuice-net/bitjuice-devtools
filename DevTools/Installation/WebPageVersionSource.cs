using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DevTools.Infrastructure;

namespace DevTools.Installation;

// Finds versions in links on a web page. Each match of the pattern is a link, relative to the page or absolute,
// its version group is the version and its file name is the asset name.
public class WebPageVersionSource(HttpClient client) : IVersionSource
{
    public const string TypeName = "web-page";

    public async Task<List<RemoteVersion>> GetVersionsAsync(VersionSource source)
    {
        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var page) || page.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException($"A web-page version source needs an https url: `{source.Url}`");

        if (string.IsNullOrWhiteSpace(source.Pattern))
            throw new InvalidOperationException($"A web-page version source needs a pattern with a version group: `{source.Url}`");

        var pattern = new Regex(source.Pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
        var versions = pattern.Matches(await GetPageAsync(page))
            .Select(i => CreateVersion(i, page))
            .Where(i => i != null)
            .GroupBy(i => i.Version)
            .Select(i => i.First())
            .OrderByDescending(i => Version.Parse(i.Version))
            .ToList();

        if (versions.Count == 0)
            throw new InvalidOperationException($"No versions matching `{source.Pattern}` found on {page}");

        return versions;
    }

    private async Task<string> GetPageAsync(Uri page)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, page);
        request.Headers.UserAgent.ParseAdd("bitjuice-devtools");

        using var response = await client.SendAsync(request);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{page} returned {(int)response.StatusCode}");

        return await response.Content.ReadAsStringAsync();
    }

    private static RemoteVersion CreateVersion(Match match, Uri page)
    {
        var version = match.Groups["version"].Value;

        // A page can write a number like 2.24-103, which names version 2.24.103.
        var number = version.Replace('-', '.');

        if (!Version.TryParse(number, out _) || !Uri.TryCreate(page, match.Value, out var link))
            return null;

        return new RemoteVersion(VersionNumber.Normalize(number), match.Value, new[] { new RemoteAsset(Path.GetFileName(link.AbsolutePath), link.AbsoluteUri, null) }, version);
    }
}
