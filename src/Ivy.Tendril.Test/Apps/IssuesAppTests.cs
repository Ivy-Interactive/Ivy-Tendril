using Ivy.Tendril.Apps.Issues;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Git;

namespace Ivy.Tendril.Test.Apps;

public class IssuesAppTests
{
    [Fact]
    public void SanitizeFileName_RemovesSpecialCharactersAndLimitsLength()
    {
        var title = "Fix: [Bug #123] Can't connect to server! (Critical & Urgent) <v2>";
        var sanitized = IssuesApp.SanitizeFileName(title);

        Assert.Equal("fix-bug-123-cant-connect-to-server-critical-urgent-v2", sanitized);

        var longTitle = "This is an extremely long issue title that exceeds the maximum sixty character filename length limit significantly";
        var longSanitized = IssuesApp.SanitizeFileName(longTitle);

        Assert.True(longSanitized.Length <= 60);
        Assert.False(longSanitized.EndsWith('-'));
        Assert.False(longSanitized.StartsWith('-'));
    }

    [Fact]
    public void SanitizeFileName_HandlesWhitespaceAndHyphens()
    {
        var input = "   Multiple   Spaces   and-Hyphens   ";
        var sanitized = IssuesApp.SanitizeFileName(input);

        Assert.Equal("multiple-spaces-and-hyphens", sanitized);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("Short body text", "Short body text")]
    public void TruncateBody_HandlesEmptyAndShortText(string? body, string expected)
    {
        var result = IssuesApp.TruncateBody(body);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void TruncateBody_TruncatesLongTextWithEllipsis()
    {
        var longText = new string('a', 600);
        var result = IssuesApp.TruncateBody(longText, maxLength: 500);

        Assert.Equal(501, result.Length); // 500 chars + 1 char for ellipsis
        Assert.EndsWith("\u2026", result);
        Assert.StartsWith(new string('a', 500), result);
    }

    [Fact]
    public void FormatGroupHeader_FormatsCorrectlyWithAndWithoutAssignee()
    {
        var singleIssueAssignee = IssuesApp.FormatGroupHeader("octocat", 1, 0);
        Assert.Equal("Found 1 issue for octocat · 0 selected", singleIssueAssignee);

        var multipleIssuesAssignee = IssuesApp.FormatGroupHeader("octocat", 5, 3);
        Assert.Equal("Found 5 issues for octocat · 3 selected", multipleIssuesAssignee);

        var singleIssueNoAssignee = IssuesApp.FormatGroupHeader((string?)null, 1, 1);
        Assert.Equal("Found 1 issue · 1 selected", singleIssueNoAssignee);

        var multipleIssuesNoAssignee = IssuesApp.FormatGroupHeader((string?)null, 10, 4);
        Assert.Equal("Found 10 issues · 4 selected", multipleIssuesNoAssignee);

        var group = new FetchedIssueGroup("john", [
            new GitHubIssue(1, "Test 1", null, [], ["john"]),
            new GitHubIssue(2, "Test 2", null, [], ["john"])
        ]);
        var groupFormatted = IssuesApp.FormatGroupHeader(group, 2);
        Assert.Equal("Found 2 issues for john · 2 selected", groupFormatted);
    }

    [Fact]
    public void GetProjectForRepo_ResolvesMatchingProjectName()
    {
        var project = new ProjectConfig { Name = "IvyFramework" };
        var stubService = new StubGithubService(project);

        var projectName = IssuesApp.GetProjectForRepo(stubService, "ivy-interactive", "ivy-framework");

        Assert.Equal("IvyFramework", projectName);
    }

    [Fact]
    public void GetProjectForRepo_ReturnsAutoWhenNoMatchingProject()
    {
        var stubService = new StubGithubService(null);

        var projectName = IssuesApp.GetProjectForRepo(stubService, "unknown-owner", "unknown-repo");

        Assert.Equal("Auto", projectName);
    }

    private sealed class StubGithubService(ProjectConfig? projectToReturn) : IGithubService
    {
        public List<RepoConfig> GetRepos() => [];
        public RepoConfig? GetRepoConfigFromPathCached(string repoPath) => null;
        public ProjectConfig? FindProjectForGithubRepo(string ownerRepo) => projectToReturn;
        public IReadOnlyList<string> GetResolvedGithubRepos(ProjectConfig project) => [];
        public Task<(List<string> assignees, string? error)> GetAssigneesAsync(string owner, string repo) =>
            Task.FromResult((new List<string>(), (string?)null));
        public Task<(List<string> labels, string? error)> GetLabelsAsync(string owner, string repo) =>
            Task.FromResult((new List<string>(), (string?)null));
        public Task<(Dictionary<string, PrInfo> statuses, string? error)> GetPrStatusesAsync(string owner, string repo) =>
            Task.FromResult((new Dictionary<string, PrInfo>(), (string?)null));
        public Task<(List<GitHubIssue> issues, string? error)> SearchIssuesAsync(IssueSearchRequest request) =>
            Task.FromResult((new List<GitHubIssue>(), (string?)null));
    }
}
