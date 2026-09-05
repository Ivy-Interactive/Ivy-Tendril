using System.Text.RegularExpressions;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Git;
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

        var selectedCategory = UseState(InboxCategory.MyIssues);
        var selectedProject = UseState<string?>(() => config.Settings.Projects.FirstOrDefault()?.Name);
        var searchQuery = UseState("");
        var selectedAssignees = UseState(Array.Empty<string>());
        var selectedLabels = UseState(Array.Empty<string>());
        var selectedIssueNumbers = UseState<HashSet<int>>([]);

        var myIssues = UseState<List<GitHubIssue>>([]);
        var reviewRequests = UseState<List<GitHubReviewItem>>([]);
        var projectIssues = UseState<List<GitHubIssue>>([]);
        var availableAssignees = UseState<List<string>>([]);
        var availableLabels = UseState<List<string>>([]);

        var isFetching = UseState(false);
        var isImporting = UseState(false);
        var errorMessage = UseState<string?>(null);
        var hasInitialLoaded = UseState(false);

        UseEffect(() =>
        {
            selectedIssueNumbers.Set([]);
            searchQuery.Set("");
            selectedAssignees.Set(Array.Empty<string>());
            selectedLabels.Set(Array.Empty<string>());

            Task.Run(FetchCurrentDataAsync);
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

        UseEffect(() =>
        {
            if (!hasInitialLoaded.Value)
            {
                hasInitialLoaded.Set(true);
                Task.Run(FetchCurrentDataAsync);

                // Also background-fetch counts for MyIssues and Reviews
                Task.Run(async () =>
                {
                    try
                    {
                        var (issues, _) = await githubService.GetMyAssignedIssuesAsync();
                        myIssues.Set(issues);
                    }
                    catch { }

                    try
                    {
                        var (reviews, _) = await githubService.GetReviewRequestsAsync();
                        reviewRequests.Set(reviews);
                    }
                    catch { }
                });
            }
        });

        async Task FetchCurrentDataAsync()
        {
            isFetching.Set(true);
            errorMessage.Set(null);

            try
            {
                if (selectedCategory.Value == InboxCategory.MyIssues)
                {
                    var (issues, err) = await githubService.GetMyAssignedIssuesAsync();
                    if (err != null)
                    {
                        errorMessage.Set(err);
                    }
                    else
                    {
                        myIssues.Set(issues);
                    }
                }
                else if (selectedCategory.Value == InboxCategory.Reviews)
                {
                    var (reviews, err) = await githubService.GetReviewRequestsAsync();
                    if (err != null)
                    {
                        errorMessage.Set(err);
                    }
                    else
                    {
                        reviewRequests.Set(reviews);
                    }
                }
                else // Project
                {
                    if (string.IsNullOrEmpty(selectedProject.Value))
                    {
                        projectIssues.Set([]);
                        return;
                    }

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

                        var (issues, err) = await githubService.SearchIssuesAsync(new IssueSearchRequest(
                            owner, repoName, Limit: 100));

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
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch inbox data");
                errorMessage.Set($"Failed to fetch data: {ex.Message}");
            }
            finally
            {
                isFetching.Set(false);
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

        var sidebar = new SidebarView(
            selectedCategory,
            selectedProject,
            config.Settings.Projects,
            myIssuesCount: myIssues.Value.Count,
            reviewsCount: reviewRequests.Value.Count,
            config: config,
            onSelectMyIssues: () => selectedCategory.Set(InboxCategory.MyIssues),
            onSelectReviews: () => selectedCategory.Set(InboxCategory.Reviews),
            onSelectProject: projName =>
            {
                selectedProject.Set(projName);
                selectedCategory.Set(InboxCategory.Project);
            }
        );

        var content = new ContentView(
            selectedCategory,
            selectedProject,
            config.Settings.Projects,
            searchQuery,
            selectedAssignees,
            selectedLabels,
            availableAssignees.Value,
            availableLabels.Value,
            selectedIssueNumbers,
            myIssues.Value,
            reviewRequests.Value,
            projectIssues.Value,
            isFetching.Value,
            errorMessage.Value,
            isImporting,
            config,
            githubService,
            onRefresh: FetchCurrentDataAsync,
            onFireOffIssues: FireOffIssues
        );

        return new SidebarLayout(
            content,
            sidebar
        ).SidebarContentScroll(Scroll.None);
    }

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
