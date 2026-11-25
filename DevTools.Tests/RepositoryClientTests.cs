using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DevTools.Sources;
using Xunit;

namespace DevTools.Tests;

public class RepositoryClientTests
{
    private class FakeHandler(HttpStatusCode status, string content) : HttpMessageHandler
    {
        public HttpRequestMessage Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(content) });
        }
    }

    [Fact]
    public async Task ListFiles_ReadsTheFilesOfAGitHubDirectory()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, """
            [
              { "name": "age.app", "type": "file" },
              { "name": "old", "type": "dir" },
              { "name": "java.app", "type": "file" }
            ]
            """);

        var files = await new RepositoryClient(new HttpClient(handler)).ListFilesAsync("owner/apps", "apps");

        Assert.Equal(new[] { "age.app", "java.app" }, files);
        Assert.Equal("https://api.github.com/repos/owner/apps/contents/apps", handler.Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task ListFiles_MissingDirectoryIsNull()
    {
        var client = new RepositoryClient(new HttpClient(new FakeHandler(HttpStatusCode.NotFound, "")));

        Assert.Null(await client.ListFilesAsync("owner/apps", "apps"));
    }

    [Fact]
    public async Task ListFiles_RateLimitAsksForAToken()
    {
        var client = new RepositoryClient(new HttpClient(new FakeHandler(HttpStatusCode.Forbidden, "")));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListFilesAsync("owner/apps", "apps"));

        Assert.Contains("githubToken", error.Message);
    }
}
