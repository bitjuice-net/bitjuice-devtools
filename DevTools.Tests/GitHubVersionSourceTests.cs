using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DevTools.App;
using Xunit;

namespace DevTools.Tests
{
    public class GitHubVersionSourceTests
    {
        private class FakeHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> respond;

            public List<HttpRequestMessage> Requests { get; } = new();

            public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            {
                this.respond = respond;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                return Task.FromResult(respond(request));
            }
        }

        private static HttpResponseMessage Json(string content)
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) };
        }

        private static string Release(string tag, bool draft = false, bool prerelease = false)
        {
            return $$"""
                { "tag_name": "{{tag}}", "draft": {{draft.ToString().ToLowerInvariant()}}, "prerelease": {{prerelease.ToString().ToLowerInvariant()}},
                  "assets": [ { "name": "a.zip", "browser_download_url": "https://x/a.zip", "digest": "sha256:ab" } ] }
                """;
        }

        [Fact]
        public async Task Releases_SkipsDraftsPrereleasesAndOtherTagsAndSortsDescending()
        {
            var body = "[" + string.Join(",", Release("v1.2.0"), Release("v1.10.0"), Release("v2.0.0", draft: true), Release("v3.0.0", prerelease: true), Release("nightly")) + "]";
            var source = new GitHubVersionSource(new HttpClient(new FakeHandler(_ => Json(body))));

            var versions = await source.GetVersionsAsync(new VersionSource { Type = "github-releases", Repo = "a/b" });

            Assert.Equal(new[] { "1.10.0", "1.2.0" }, versions.Select(i => i.Version));
            Assert.Equal("v1.10.0", versions[0].Tag);
            Assert.Equal("sha256:ab", versions[0].Assets.Single().Digest);
        }

        [Fact]
        public async Task Tags_UsesCustomPatternAndFollowsPages()
        {
            var firstPage = "[" + string.Join(",", Enumerable.Range(0, 100).Select(i => $$"""{ "name": "rel-1.{{i}}.0" }""")) + "]";
            var handler = new FakeHandler(request => Json(request.RequestUri.Query.EndsWith("&page=1") ? firstPage : """[ { "name": "rel-2.0.0" } ]"""));
            var source = new GitHubVersionSource(new HttpClient(handler));

            var versions = await source.GetVersionsAsync(new VersionSource { Type = "github-tags", Repo = "a/b", Tag = @"^rel-(?<version>\d+\.\d+\.\d+)$" });

            Assert.Equal(2, handler.Requests.Count);
            Assert.Equal(101, versions.Count);
            Assert.Equal("2.0.0", versions[0].Version);
        }

        [Fact]
        public async Task PaginationLimit_KeepsVersionsFromEarlierPages()
        {
            var firstPage = "[" + string.Join(",", Enumerable.Range(0, 100).Select(i => $$"""{ "name": "v1.{{i}}.0" }""")) + "]";
            var handler = new FakeHandler(request => request.RequestUri.Query.EndsWith("&page=1") ? Json(firstPage) : new HttpResponseMessage(HttpStatusCode.UnprocessableEntity));
            var source = new GitHubVersionSource(new HttpClient(handler));

            var versions = await source.GetVersionsAsync(new VersionSource { Type = "github-tags", Repo = "a/b" });

            Assert.Equal(100, versions.Count);
        }

        [Fact]
        public async Task UnprocessableFirstPage_Throws()
        {
            var source = new GitHubVersionSource(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity))));

            await Assert.ThrowsAsync<InvalidOperationException>(() => source.GetVersionsAsync(new VersionSource { Type = "github-tags", Repo = "a/b" }));
        }

        [Fact]
        public async Task SendsTokenOnlyWhenSet()
        {
            var handler = new FakeHandler(_ => Json("[]"));
            var source = new GitHubVersionSource(new HttpClient(handler));
            var original = Environment.GetEnvironmentVariable("GITHUB_TOKEN");

            try
            {
                Environment.SetEnvironmentVariable("GITHUB_TOKEN", null);
                await source.GetVersionsAsync(new VersionSource { Type = "github-tags", Repo = "a/b" });
                Environment.SetEnvironmentVariable("GITHUB_TOKEN", "secret");
                await source.GetVersionsAsync(new VersionSource { Type = "github-tags", Repo = "a/b" });
            }
            finally
            {
                Environment.SetEnvironmentVariable("GITHUB_TOKEN", original);
            }

            Assert.Null(handler.Requests[0].Headers.Authorization);
            Assert.Equal("secret", handler.Requests[1].Headers.Authorization.Parameter);
        }

        [Theory]
        [InlineData("github-tags", "../../evil")]
        [InlineData("gitlab", "a/b")]
        public async Task RejectsInvalidSource(string type, string repo)
        {
            var source = new GitHubVersionSource(new HttpClient(new FakeHandler(_ => Json("[]"))));

            await Assert.ThrowsAsync<InvalidOperationException>(() => source.GetVersionsAsync(new VersionSource { Type = type, Repo = repo }));
        }

        [Fact]
        public async Task RateLimit_ExplainsToken()
        {
            var source = new GitHubVersionSource(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden))));

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => source.GetVersionsAsync(new VersionSource { Type = "github-tags", Repo = "a/b" }));

            Assert.Contains("GITHUB_TOKEN", error.Message);
        }
    }
}
