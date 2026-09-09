using System.Text.RegularExpressions;
using Ivy;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Git;
using Ivy.Tendril.Services.Inbox;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Apps.Inbox;

[App(title: "Inbox", icon: Icons.Inbox, group: ["Apps"], order: Constants.Inbox)]
public class InboxApp : ViewBase
{
    public override object Build()
    {
        var githubService = UseService<IGithubService>();
        var config = UseService<IConfigService>();
        var client = UseService<IClientProvider>();
        var logger = UseService<ILogger<InboxApp>>();
        var autoImportService = UseService<AssignedIssuesAutoImportService>();

        var selectedCategory = UseState(InboxCategory.MyIssues);
        var selectedProject = UseState<string?>(() => config.Settings.Projects.FirstOrDefault()?.Name);
        var searchQuery = UseState("");
        var selectedAssignees = UseState(Array.Empty<string>());
        var selectedLabels = UseState(Array.Empty<string>());
        var selectedIssueNumbers = UseState<HashSet<int>>([]);

        var myIssuesQuery = this.UseQuery<(List<GitHubIssue> Issues, string? Error), string>(
            "inbox:my-issues",
            async (_, _) => await githubService.GetMyAssignedIssuesAsync(),
            new QueryOptions { Expiration = TimeSpan.FromSeconds(60) },
            tags: [GithubService.MyIssuesQueryTag]);
        var reviewRequestsQuery = this.UseQuery<(List<GitHubReviewItem> Reviews, string? Error), string>(
            "inbox:review-requests",
            async (_, _) => await githubService.GetReviewRequestsAsync(),
            new QueryOptions { Expiration = TimeSpan.FromSeconds(60) },
            tags: [GithubService.ReviewRequestsQueryTag]);

        var projectIssues = UseState<List<GitHubIssue>>([]);
        var availableAssignees = UseState<List<string>>([]);
        var availableLabels = UseState<List<string>>([]);

        var isFetching = UseState(false);
        var isImporting = UseState(false);
        var errorMessage = UseState<string?>(null);
        var refreshToken = UseRefreshToken();

        UseEffect(() =>
        {
            selectedIssueNumbers.Set([]);
            searchQuery.Set("");
            selectedAssignees.Set(Array.Empty<string>());
            selectedLabels.Set(Array.Empty<string>());

            if (selectedCategory.Value == InboxCategory.Project)
            {
                Task.Run(FetchCurrentDataAsync);
            }
        }, selectedCategory);

        UseEffect(() =>
        {
            if (selectedCategory.Value == InboxCategory.Project)
            {
                selectedIssueNumbers.Set([]);
                searchQuery.Set("");
                selectedAssignees.Set(Array.Empty<string>());
                selectedLabels.Set(Array.Empty<string>());

                Task.Run(FetchCurrentDataAsync);
            }
        }, selectedProject);

        async Task FetchCurrentDataAsync()
        {
            if (string.IsNullOrEmpty(selectedProject.Value))
            {
                projectIssues.Set([]);
                return;
            }

            isFetching.Set(true);
            errorMessage.Set(null);

            try
            {
                var proj = config.Settings.Projects.FirstOrDefault(p =>
                    string.Equals(p.Name, selectedProject.Value, StringComparison.OrdinalIgnoreCase));
                if (proj == null)
                {
                    projectIssues.Set([]);
                    return;
                }

                var resolvedRepos = githubService.GetResolvedGithubRepos(proj);
                if (resolvedRepos.Count == 0)
                {
                    projectIssues.Set([]);
                    errorMessage.Set($"No git remotes resolved for project {proj.Name}.");
                    return;
                }

                var allIssues = new List<GitHubIssue>();
                var allAssignees = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var allLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var repoStr in resolvedRepos)
                {
                    var parts = repoStr.Split('/');
                    if (parts.Length != 2) continue;
                    var owner = parts[0];
                    var repoName = parts[1];

                    var (issues, err) = await githubService.SearchIssuesAsync(
                        CreateProjectIssueRequest(owner, repoName));

                    if (err != null && errorMessage.Value == null)
                        errorMessage.Set(err);

                    foreach (var issue in issues)
                    {
                        allIssues.Add(issue with { Repository = repoStr });
                        foreach (var a in issue.Assignees) if (!string.IsNullOrWhiteSpace(a)) allAssignees.Add(a);
                        foreach (var l in issue.Labels) if (!string.IsNullOrWhiteSpace(l)) allLabels.Add(l);
                    }
                }

                projectIssues.Set(allIssues);
                availableAssignees.Set(allAssignees.OrderBy(a => a).ToList());
                availableLabels.Set(allLabels.OrderBy(l => l).ToList());
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch inbox data");
                errorMessage.Set($"Failed to fetch data: {ex.Message}");
            }
            finally
            {
                isFetching.Set(false);
                refreshToken.Refresh();
            }
        }

        async Task FireOffIssues(IReadOnlyList<GitHubIssue> issuesToFire)
        {
            if (issuesToFire.Count == 0) return;

            var distinctIssues = issuesToFire
                .DistinctBy(i => i.Number)
                .ToList();
            if (distinctIssues.Count == 0) return;

            isImporting.Set(true);
            try
            {
                var inboxPath = Path.Combine(config.TendrilHome, "Inbox");
                Directory.CreateDirectory(inboxPath);

                var importedCount = 0;

                foreach (var issue in distinctIssues)
                {
                    var safeName = SanitizeFileName(issue.Title);
                    var fileName = $"{issue.Number}-{safeName}.md";
                    var filePath = Path.Combine(inboxPath, fileName);

                    if (File.Exists(filePath)) continue;

                    var targetProject = selectedCategory.Value == InboxCategory.Project && !string.IsNullOrEmpty(selectedProject.Value)
                        ? selectedProject.Value
                        : (issue.Repository != null ? githubService.FindProjectForGithubRepo(issue.Repository)?.Name : null) ?? "Auto";

                    var issueUrl = issue.Url ?? (issue.Repository != null
                        ? $"https://github.com/{issue.Repository}/issues/{issue.Number}"
                        : "");

                    var content = $"""
                                   ---
                                   project: {targetProject}
                                   ---
                                   {(string.IsNullOrEmpty(issueUrl) ? $"# Issue #{issue.Number}: {issue.Title}" : $"[GitHub Issue #{issue.Number}]({issueUrl})")}

                                   {issue.Body}
                                   """;

                    await FileHelper.WriteAllTextAsync(filePath, content);
                    importedCount++;
                }

                selectedIssueNumbers.Set(prev =>
                {
                    var next = new HashSet<int>(prev);
                    foreach (var i in distinctIssues) next.Remove(i.Number);
                    return next;
                });
                refreshToken.Refresh();

                client.Toast($"Fired off {importedCount} issue{(importedCount == 1 ? "" : "s")} in Tendril", "Inbox");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fire off issues");
                client.Toast($"Failed to fire off issues: {ex.Message}", "Error");
            }
            finally
            {
                isImporting.Set(false);
            }
        }

        var myIssuesCount = myIssuesQuery.Loading ? 0 : (myIssuesQuery.Value.Issues?.Count ?? 0);
        var reviewsCount = reviewRequestsQuery.Loading ? 0 : (reviewRequestsQuery.Value.Reviews?.Count ?? 0);

        var sidebar = new SidebarView(
            selectedCategory,
            selectedProject,
            config.Settings.Projects,
            myIssuesCount: myIssuesCount,
            reviewsCount: reviewsCount,
            config: config,
            onSelectMyIssues: () => selectedCategory.Set(InboxCategory.MyIssues),
            onSelectReviews: () => selectedCategory.Set(InboxCategory.Reviews),
            onSelectProject: projName =>
            {
                selectedProject.Set(projName);
                selectedCategory.Set(InboxCategory.Project);
            }
        );

        var myIssuesList = myIssuesQuery.Value.Issues ?? [];
        var reviewRequestsList = reviewRequestsQuery.Value.Reviews ?? [];

        var currentIsFetching = selectedCategory.Value switch
        {
            InboxCategory.MyIssues => myIssuesQuery.Loading,
            InboxCategory.Reviews => reviewRequestsQuery.Loading,
            _ => isFetching.Value
        };

        var currentErrorMessage = selectedCategory.Value switch
        {
            InboxCategory.MyIssues => myIssuesQuery.Value.Error,
            InboxCategory.Reviews => reviewRequestsQuery.Value.Error,
            _ => errorMessage.Value
        };

        Func<Task> onRefresh = selectedCategory.Value switch
        {
            InboxCategory.MyIssues => () => { myIssuesQuery.Mutator.Revalidate(); return Task.CompletedTask; }
            ,
            InboxCategory.Reviews => () => { reviewRequestsQuery.Mutator.Revalidate(); return Task.CompletedTask; }
            ,
            _ => FetchCurrentDataAsync
        };

        var content = new ContentView(
            selectedCategory,
            selectedProject,
            config.Settings.Projects,
            selectedIssueNumbers,
            myIssuesList,
            reviewRequestsList,
            projectIssues.Value,
            currentIsFetching,
            currentErrorMessage,
            isImporting,
            config,
            githubService,
            refreshToken,
            onRefresh: onRefresh,
            onFireOffIssues: FireOffIssues,
            autoImportService: autoImportService
        );

        return new SidebarLayout(
            content,
            sidebar
        );
    }

    public static IssueSearchRequest CreateProjectIssueRequest(string owner, string repoName) =>
        new(owner, repoName, Limit: GithubService.DefaultIssueLimit);

    public static string GetProjectForRepo(IGithubService githubService, string owner, string repo)
    {
        return githubService.FindProjectForGithubRepo($"{owner}/{repo}")?.Name ?? "Auto";
    }

    public static string SanitizeFileName(string title)
    {
        var sanitized = Regex.Replace(title, @"[^a-zA-Z0-9\s-]", "");
        sanitized = Regex.Replace(sanitized, @"\s+", "-");
        sanitized = sanitized.Trim('-').ToLowerInvariant();
        return sanitized.Length > 60 ? sanitized[..60].TrimEnd('-') : sanitized;
    }

    public static string TruncateBody(string? body, int maxLength = 500)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";
        var trimmed = body.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength] + "\u2026";
    }

    public static string FormatGroupHeader(FetchedIssueGroup group, int selectedCount) =>
        FormatGroupHeader(group.Assignee, group.Issues.Count, selectedCount);

    public static string FormatGroupHeader(string? assignee, int issueCount, int selectedCount)
    {
        var issueLabel = issueCount == 1 ? "issue" : "issues";
        if (assignee is { } name)
            return $"Found {issueCount} {issueLabel} for {name} · {selectedCount} selected";

        return $"Found {issueCount} {issueLabel} · {selectedCount} selected";
    }
}

public sealed record FetchedIssueGroup(string? Assignee, IReadOnlyList<GitHubIssue> Issues);
