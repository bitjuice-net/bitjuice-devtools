using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DevTools.Installation;
using DevTools.Installation.Models;
using Xunit;

namespace DevTools.Tests;

public class WebPageVersionSourceTests
{
    private const string PageUrl = "https://example.test/tool/downloads.html";
    private const string LinkPattern = @"windows/releases/(?<version>[\d.]+)/tool-64-bit-\k<version>\.7z";

    private class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(respond(request));
        }
    }

    private static WebPageVersionSource Source(string page, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new WebPageVersionSource(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(status) { Content = new StringContent(page) })));
    }

    private static VersionSource Definition(string pattern = LinkPattern, string url = PageUrl)
    {
        return new VersionSource { Type = "web-page", Url = url, Pattern = pattern };
    }

    [Fact]
    public async Task FindsVersionAndResolvesRelativeLink()
    {
        var page = """
                   <a href="windows/releases/103.0/tool-64-bit-103.0-setup.exe">installer</a>
                   <a href="windows/releases/103.0/tool-64-bit-103.0.7z">portable</a>
                   """;

        var version = Assert.Single(await Source(page).GetVersionsAsync(Definition()));

        Assert.Equal("103.0.0", version.Version);
        var asset = Assert.Single(version.Assets);
        Assert.Equal("tool-64-bit-103.0.7z", asset.Name);
        Assert.Equal("https://example.test/tool/windows/releases/103.0/tool-64-bit-103.0.7z", asset.Url);
        Assert.Null(asset.Digest);
    }

    [Fact]
    public async Task VersionIsNormalizedAndKeepsItsFormFromThePage()
    {
        var version = Assert.Single(await Source("windows/releases/103.0/tool-64-bit-103.0.7z").GetVersionsAsync(Definition()));

        Assert.Equal("103.0.0", version.Version);
        Assert.Equal("103.0", version.SourceVersion);
    }

    [Fact]
    public async Task DashesBecomeDotsAndSourceFormIsKept()
    {
        var page = """
                   <a href="/ci/tool-2.24-103-gdee49fc.zip">x</a>
                   <a href="/ci/tool-2.24-94-g9c88bc1.zip">x</a>
                   <a href="/ci/tool-2.21-32-g04368b2.zip">x</a>
                   """;

        var versions = await Source(page).GetVersionsAsync(Definition(@"/ci/tool-(?<version>\d+\.\d+-\d+)-g[0-9a-f]+\.zip"));

        Assert.Equal(new[] { "2.24.103", "2.24.94", "2.21.32" }, versions.Select(i => i.Version));
        Assert.Equal("2.24-103", versions[0].SourceVersion);
        Assert.Equal("https://example.test/ci/tool-2.24-103-gdee49fc.zip", versions[0].Assets[0].Url);
    }

    [Fact]
    public async Task VersionCapturedInPartsIsJoinedWithDots()
    {
        var page = """
                   <a href="https://cdn.test/files/hwi_860.zip">x</a>
                   <a href="https://cdn.test/files/hwi_858.zip">x</a>
                   """;

        var versions = await Source(page).GetVersionsAsync(Definition(@"https://cdn\.test/files/hwi_(?<version>\d)(?<version>\d\d)\.zip"));

        Assert.Equal(new[] { "8.60.0", "8.58.0" }, versions.Select(i => i.Version));
        Assert.Equal("860", versions[0].SourceVersion);
        Assert.Equal("hwi_860.zip", versions[0].Assets[0].Name);
    }

    [Fact]
    public async Task KeepsAbsoluteLink()
    {
        var page = """<a href="https://cdn.test/1.2.0/tool.zip">x</a>""";

        var version = Assert.Single(await Source(page).GetVersionsAsync(Definition(@"https://cdn\.test/(?<version>[\d.]+)/tool\.zip")));

        Assert.Equal("https://cdn.test/1.2.0/tool.zip", version.Assets[0].Url);
    }

    [Fact]
    public async Task RemovesDuplicatesAndSortsDescending()
    {
        var page = string.Join(" ", new[] { "9.0", "103.0", "9.0", "10.1.2" }.Select(i => $"windows/releases/{i}/tool-64-bit-{i}.7z"));

        var versions = await Source(page).GetVersionsAsync(Definition());

        Assert.Equal(new[] { "103.0.0", "10.1.2", "9.0.0" }, versions.Select(i => i.Version));
    }

    [Theory]
    [InlineData("<a href='other.zip'>x</a>")]
    [InlineData("windows/releases/1.0-beta/tool-64-bit-1.0-beta.7z")]
    public async Task NoMatchingVersionThrowsWithPageUrl(string page)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Source(page).GetVersionsAsync(Definition(@"windows/releases/(?<version>[^/]+)/tool-64-bit-\k<version>\.7z")));

        Assert.Contains(PageUrl, error.Message);
    }

    [Fact]
    public async Task HttpErrorThrows()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Source("", HttpStatusCode.NotFound).GetVersionsAsync(Definition()));

        Assert.Contains("404", error.Message);
    }

    [Theory]
    [InlineData("http://example.test/page.html")]
    [InlineData("not a url")]
    [InlineData(null)]
    public async Task NonHttpsUrlIsRejected(string url)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Source("").GetVersionsAsync(Definition(url: url)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task MissingPatternIsRejected(string pattern)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Source("").GetVersionsAsync(new VersionSource { Type = "web-page", Url = PageUrl, Pattern = pattern }));
    }

    [Fact]
    public async Task SendsUserAgent()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("windows/releases/1.0/tool-64-bit-1.0.7z") });

        await new WebPageVersionSource(new HttpClient(handler)).GetVersionsAsync(Definition());

        Assert.Equal(PageUrl, handler.Request.RequestUri.AbsoluteUri);
        Assert.Contains("bitjuice-devtools", handler.Request.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task RouterSelectsSourceByType()
    {
        var handler = new FakeHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri.Host == "api.github.com" ? """[ { "name": "v2.0.0" } ]""" : "windows/releases/1.0/tool-64-bit-1.0.7z")
        });
        var client = new HttpClient(handler);
        var router = new VersionSourceRouter(new GitHubVersionSource(client), new WebPageVersionSource(client));

        Assert.Equal("1.0.0", Assert.Single(await router.GetVersionsAsync(Definition())).Version);
        Assert.Equal("2.0.0", Assert.Single(await router.GetVersionsAsync(new VersionSource { Type = "github-tags", Repo = "a/b" })).Version);
        await Assert.ThrowsAsync<InvalidOperationException>(() => router.GetVersionsAsync(new VersionSource { Type = "ftp" }));
    }
}
