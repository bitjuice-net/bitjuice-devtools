using System;
using System.Linq;

namespace DevTools.Infrastructure;

public class Settings
{
    public string RootPath { get; set; } = "..";

    // Raises the GitHub API rate limit and opens private manifest repositories, GITHUB_TOKEN when not set.
    public string GitHubToken { get; set; }

    public const string GitHubTokenVariable = "GITHUB_TOKEN";

    // The token of the settings, then the one of the environment, null when there is none.
    public string ResolveGitHubToken()
    {
        return new[] { GitHubToken, Environment.GetEnvironmentVariable(GitHubTokenVariable) }.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i));
    }
}