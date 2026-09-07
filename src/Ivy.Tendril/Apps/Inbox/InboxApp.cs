using System.Text.RegularExpressions;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Git;
using Ivy.Tendril.Services.IssueTrackers;
using Ivy.Tendril.Services.IssueTrackers.Models;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Apps.Inbox;

[App(title: "Inbox", icon: Icons.Inbox, group: ["Apps"], order: Constants.Inbox)]
public class InboxApp : ViewBase
{
    public override object Build()
    {
        var issueTrackerService = UseService<IIssueTrackerService>();
        var githubService = UseService<IGithubService>();
        var config = UseService<IConfigService>();
        var client = UseService<IClientProvider>();
        var logger = UseService<ILogger<InboxApp>>();

        var selectedCategory = UseState(InboxCategory.MyIssues);
        var selectedProject = UseState<string?>(() => config.Settings.Projects.FirstOrDefault()?.Name);
        var searchQuery = UseState("");
        var selectedAssignees = UseState(Array.Empty<string>());
        var selectedLabels = UseState(Array.Empty<string>());
        var selectedIssueIds = UseState<HashSet<string>>([]);

        var myIssues = UseState<List<TrackerIssue>>([]);
        var reviewRequests = UseState<List<TrackerReviewItem>>([]);
        var projectIssues = UseState<List<TrackerIssue>>([]);

        var isFetching = UseState(false);
        var isImporting = UseState(false);
        var errorMessage = UseState<string?>(null);
        var hasInitialLoaded = UseState(false);
        var refreshToken = UseRefreshToken();

        UseEffect(() =>
        {
            selectedIssueIds.Set([]);
            searchQuery.Set("");
            selectedAssignees.Set(Array.Empty<string>());
            selectedLabels.Set(Array.Empty<string>());

            Task.Run(FetchCurrentDataAsync);
        }, selectedCategory);

        UseEffect(() =>
        {
            if (selectedCategory.Value == InboxCategory.Project)
            {
                selectedIssueIds.Set([]);
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

                // Background-fetch counts for MyIssues and Reviews
                Task.Run(async () =>
                {
                    try
                    {
                        var (issues, _) = await issueTrackerService.GetMyAssignedIssuesAsync();
                        myIssues.Set(issues);
                    }
                    catch { }

                    try
                    {
                        var (reviews, _) = await issueTrackerService.GetReviewRequestsAsync();
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
                    var (issues, errors) = await issueTrackerService.GetMyAssignedIssuesAsync();
                    if (errors.Count > 0 && issues.Count == 0)
                    {
                        errorMessage.Set(string.Join("\n", errors));
                    }
                    else
                    {
                        myIssues.Set(issues);
                    }
                }
                else if (selectedCategory.Value == InboxCategory.Reviews)
                {
                    var (reviews, errors) = await issueTrackerService.GetReviewRequestsAsync();
                    if (errors.Count > 0 && reviews.Count == 0)
                    {
                        errorMessage.Set(string.Join("\n", errors));
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

                    var (issues, errors) = await issueTrackerService.GetProjectIssuesAsync(proj, new TrackerIssueQuery());
                    if (errors.Count > 0 && issues.Count == 0)
                    {
                        errorMessage.Set(string.Join("\n", errors));
                        projectIssues.Set([]);
                    }
                    else
                    {
                        projectIssues.Set(issues);
                    }
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
                refreshToken.Refresh();
            }
        }

        async Task FireOffIssues(IReadOnlyList<TrackerIssue> issuesToFire)
        {
            if (issuesToFire.Count == 0) return;

            var distinctIssues = issuesToFire
                .DistinctBy(i => i.Id)
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
                    var safeKey = SanitizeKey(issue.Key);
                    var safeName = SanitizeFileName(issue.Title);
                    var fileName = $"{safeKey}-{safeName}.md";
                    var filePath = Path.Combine(inboxPath, fileName);

                    if (File.Exists(filePath)) continue;

                    var targetProject = selectedCategory.Value == InboxCategory.Project && !string.IsNullOrEmpty(selectedProject.Value)
                        ? selectedProject.Value
                        : (issue.Scope != null ? FindProjectForScope(config, githubService, issue.Scope, issue.ProviderId)?.Name : null) ?? "Auto";

                    var providerTitle = issue.ProviderId switch
                    {
                        "jira" => "Jira",
                        "linear" => "Linear",
                        "github" => "GitHub",
                        "gitlab" => "GitLab",
                        _ => issue.ProviderId
                    };

                    var issueHeader = !string.IsNullOrEmpty(issue.Url)
                        ? $"[{providerTitle} Issue {issue.Key}]({issue.Url})"
                        : $"# Issue {issue.Key}: {issue.Title}";

                    var content = $"""
                                   ---
                                   project: {targetProject}
                                   ---
                                   {issueHeader}

                                   {issue.Body}
                                   """;

                    await FileHelper.WriteAllTextAsync(filePath, content);
                    importedCount++;
                }

                selectedIssueIds.Set(prev =>
                {
                    var next = new HashSet<string>(prev);
                    foreach (var i in distinctIssues) next.Remove(i.Id);
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
            selectedIssueIds,
            myIssues.Value,
            reviewRequests.Value,
            projectIssues.Value,
            isFetching.Value,
            errorMessage.Value,
            isImporting,
            config,
            refreshToken,
            onRefresh: FetchCurrentDataAsync,
            onFireOffIssues: FireOffIssues
        );

        return new SidebarLayout(
            content,
            sidebar
        );
    }

    public static ProjectConfig? FindProjectForScope(IConfigService config, IGithubService githubService, string scope, string providerId)
    {
        var explicitMatch = config.Settings.Projects.FirstOrDefault(p =>
            p.IssueTracker != null &&
            (string.Equals(p.IssueTracker.Repo, scope, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(p.IssueTracker.ProjectKey, scope, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(p.IssueTracker.TeamKey, scope, StringComparison.OrdinalIgnoreCase)));
        if (explicitMatch != null) return explicitMatch;

        if (string.Equals(providerId, "github", StringComparison.OrdinalIgnoreCase))
        {
            return githubService.FindProjectForGithubRepo(scope);
        }

        return null;
    }

    public static string GetProjectForRepo(IGithubService githubService, string owner, string repo)
    {
        return githubService.FindProjectForGithubRepo($"{owner}/{repo}")?.Name ?? "Auto";
    }

    public static string SanitizeKey(string key)
    {
        var trimmed = key.TrimStart('#');
        var sanitized = Regex.Replace(trimmed, @"[^a-zA-Z0-9_-]", "");
        return string.IsNullOrEmpty(sanitized) ? "issue" : sanitized;
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

public sealed record FetchedIssueGroup(string? Assignee, IReadOnlyList<TrackerIssue> Issues);
