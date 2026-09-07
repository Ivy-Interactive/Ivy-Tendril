using Ivy.Tendril.Apps.Inbox;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Git;

namespace Ivy.Tendril.Test.Apps;

public class InboxAppTests
{
    [Fact]
    public void SanitizeFileName_RemovesSpecialCharactersAndLimitsLength()
    {
        var title = "Fix: [Bug #123] Can't connect to server! (Critical & Urgent) <v2>";
        var sanitized = InboxApp.SanitizeFileName(title);

        Assert.Equal("fix-bug-123-cant-connect-to-server-critical-urgent-v2", sanitized);

        var longTitle = "This is an extremely long issue title that exceeds the maximum sixty character filename length limit significantly";
        var longSanitized = InboxApp.SanitizeFileName(longTitle);

        Assert.True(longSanitized.Length <= 60);
        Assert.False(longSanitized.EndsWith('-'));
        Assert.False(longSanitized.StartsWith('-'));
    }

    [Fact]
    public void SanitizeFileName_HandlesWhitespaceAndHyphens()
    {
        var input = "   Multiple   Spaces   and-Hyphens   ";
        var sanitized = InboxApp.SanitizeFileName(input);

        Assert.Equal("multiple-spaces-and-hyphens", sanitized);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("Short body text", "Short body text")]
    public void TruncateBody_HandlesEmptyAndShortText(string? body, string expected)
    {
        var result = InboxApp.TruncateBody(body);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void TruncateBody_TruncatesLongTextWithEllipsis()
    {
        var longText = new string('a', 600);
        var result = InboxApp.TruncateBody(longText, maxLength: 500);

        Assert.Equal(501, result.Length); // 500 chars + 1 char for ellipsis
        Assert.EndsWith("\u2026", result);
        Assert.StartsWith(new string('a', 500), result);
    }

    [Fact]
    public void FormatGroupHeader_FormatsCorrectlyWithAndWithoutAssignee()
    {
        var singleIssueAssignee = InboxApp.FormatGroupHeader("octocat", 1, 0);
        Assert.Equal("Found 1 issue for octocat · 0 selected", singleIssueAssignee);

        var multipleIssuesAssignee = InboxApp.FormatGroupHeader("octocat", 5, 3);
        Assert.Equal("Found 5 issues for octocat · 3 selected", multipleIssuesAssignee);

        var singleIssueNoAssignee = InboxApp.FormatGroupHeader((string?)null, 1, 1);
        Assert.Equal("Found 1 issue · 1 selected", singleIssueNoAssignee);

        var multipleIssuesNoAssignee = InboxApp.FormatGroupHeader((string?)null, 10, 4);
        Assert.Equal("Found 10 issues · 4 selected", multipleIssuesNoAssignee);

        var group = new FetchedIssueGroup("john", [
            new GitHubIssue(1, "Test 1", null, [], ["john"]),
            new GitHubIssue(2, "Test 2", null, [], ["john"])
        ]);
        var groupFormatted = InboxApp.FormatGroupHeader(group, 2);
        Assert.Equal("Found 2 issues for john · 2 selected", groupFormatted);
    }

    [Fact]
    public void GetProjectForRepo_ResolvesMatchingProjectName()
    {
        var project = new ProjectConfig { Name = "IvyFramework" };
        var stubService = new StubGithubService(project);

        var projectName = InboxApp.GetProjectForRepo(stubService, "ivy-interactive", "ivy-framework");

        Assert.Equal("IvyFramework", projectName);
    }

    [Fact]
    public void GetProjectForRepo_ReturnsAutoWhenNoMatchingProject()
    {
        var stubService = new StubGithubService(null);

        var projectName = InboxApp.GetProjectForRepo(stubService, "unknown-owner", "unknown-repo");

        Assert.Equal("Auto", projectName);
    }

    [Fact]
    public void ParseIssuesFromJson_ParsesRepositoryAndUrl()
    {
        var json = """
                   [
                     {
                       "number": 101,
                       "title": "Test Issue",
                       "body": "Issue description",
                       "labels": [{"name": "bug"}],
                       "assignees": [{"login": "alice"}],
                       "repository": {"nameWithOwner": "owner/repo"},
                       "url": "https://github.com/owner/repo/issues/101",
                       "updatedAt": "2026-09-01T12:00:00Z"
                     }
                   ]
                   """;

        var issues = GithubService.ParseIssuesFromJson(json);

        Assert.Single(issues);
        var issue = issues[0];
        Assert.Equal(101, issue.Number);
        Assert.Equal("Test Issue", issue.Title);
        Assert.Equal("owner/repo", issue.Repository);
        Assert.Equal("https://github.com/owner/repo/issues/101", issue.Url);
        Assert.Single(issue.Labels);
        Assert.Equal("bug", issue.Labels[0]);
        Assert.Single(issue.Assignees);
        Assert.Equal("alice", issue.Assignees[0]);
    }

    [Fact]
    public void ParseReviewsFromJson_ParsesPrDetailsAndBranch()
    {
        var json = """
                   [
                     {
                       "number": 202,
                       "title": "Test PR",
                       "body": "PR description",
                       "labels": [],
                       "assignees": [],
                       "repository": {"nameWithOwner": "owner/repo"},
                       "url": "https://github.com/owner/repo/pull/202",
                       "headRefName": "feature/awesome",
                       "updatedAt": "2026-09-02T12:00:00Z"
                     }
                   ]
                   """;

        var reviews = GithubService.ParseReviewsFromJson(json);

        Assert.Single(reviews);
        var pr = reviews[0];
        Assert.Equal(202, pr.Number);
        Assert.Equal("Test PR", pr.Title);
        Assert.Equal("owner/repo", pr.Repository);
        Assert.Equal("feature/awesome", pr.Branch);
        Assert.Equal("https://github.com/owner/repo/pull/202", pr.Url);
    }

    [Fact]
    public void ParseReviewsFromJson_HandlesMissingHeadRefName()
    {
        var json = """
                   [
                     {
                       "number": 303,
                       "title": "Review Request without HeadRefName",
                       "body": null,
                       "labels": [],
                       "assignees": [],
                       "repository": {"nameWithOwner": "owner/repo"},
                       "url": "https://github.com/owner/repo/pull/303",
                       "updatedAt": "2026-09-05T10:00:00Z"
                     }
                   ]
                   """;

        var reviews = GithubService.ParseReviewsFromJson(json);

        Assert.Single(reviews);
        var pr = reviews[0];
        Assert.Equal(303, pr.Number);
        Assert.Equal("Review Request without HeadRefName", pr.Title);
        Assert.Null(pr.Branch);
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
        public Task<(List<GitHubIssue> issues, string? error)> GetMyAssignedIssuesAsync() =>
            Task.FromResult((new List<GitHubIssue>(), (string?)null));
        public Task<(List<GitHubReviewItem> prs, string? error)> GetReviewRequestsAsync() =>
            Task.FromResult((new List<GitHubReviewItem>(), (string?)null));
    }
}
