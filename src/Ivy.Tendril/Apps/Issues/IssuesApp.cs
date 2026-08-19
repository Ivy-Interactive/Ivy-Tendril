using System.Text.RegularExpressions;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Git;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Apps.Issues;

public sealed record FetchedIssueGroup(string? Assignee, IReadOnlyList<GitHubIssue> Issues);

[App(title: "Issues", icon: Icons.CircleDot, group: ["Apps"], order: Constants.Issues)]
public class IssuesApp : ViewBase
{
    public override object Build()
    {
        var githubService = UseService<IGithubService>();
        var config = UseService<IConfigService>();
        var client = UseService<IClientProvider>();
        var logger = UseService<ILogger<IssuesApp>>();

        var selectedRepo = UseState<string?>(null);
        var searchQuery = UseState("");
        var selectedAssignees = UseState(Array.Empty<string>());
        var selectedLabels = UseState(Array.Empty<string>());
        var fetchedIssueGroups = UseState<IReadOnlyList<FetchedIssueGroup>?>(null);
        var selectedIssueNumbers = UseState<HashSet<int>>([]);
        var activeIssue = UseState<GitHubIssue?>(null);
        var filtersOpen = UseState(false);
        var errorMessage = UseState<string?>(null);
        var resultsTruncated = UseState(false);
        var isFetching = UseState(false);
        var isImporting = UseState(false);

        UseEffect(() =>
        {
            fetchedIssueGroups.Set(null);
            selectedIssueNumbers.Set([]);
            activeIssue.Set(null);
            errorMessage.Set(null);
            resultsTruncated.Set(false);
            selectedAssignees.Set(Array.Empty<string>());
            selectedLabels.Set(Array.Empty<string>());
        }, selectedRepo);

        List<RepoConfig> repos;
        try
        {
            repos = githubService.GetRepos();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Exception loading repos");
            repos = [];
        }

        var allFetchedIssues = fetchedIssueGroups.Value?
            .SelectMany(g => g.Issues)
            .DistinctBy(i => i.Number)
            .ToList() ?? [];

        if (activeIssue.Value == null && allFetchedIssues.Count > 0)
        {
            activeIssue.Set(allFetchedIssues[0]);
        }
        else if (activeIssue.Value != null && allFetchedIssues.All(i => i.Number != activeIssue.Value.Number))
        {
            activeIssue.Set(allFetchedIssues.FirstOrDefault());
        }

        async Task FetchIssues()
        {
            if (selectedRepo.Value is not { } repoName) return;
            var repo = repos.FirstOrDefault(r => r.DisplayName == repoName);
            if (repo is null) return;

            isFetching.Set(true);
            errorMessage.Set(null);
            resultsTruncated.Set(false);
            fetchedIssueGroups.Set(null);
            selectedIssueNumbers.Set([]);
            activeIssue.Set(null);

            try
            {
                var labels = selectedLabels.Value.Length > 0 ? selectedLabels.Value : null;
                var query = string.IsNullOrWhiteSpace(searchQuery.Value) ? null : searchQuery.Value;
                var assigneeFilters = selectedAssignees.Value
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .ToArray();

                var groups = new List<FetchedIssueGroup>();
                if (assigneeFilters.Length == 0)
                {
                    var (issues, error) = await githubService.SearchIssuesAsync(new IssueSearchRequest(
                        repo.Owner, repo.Name, query, null, labels));
                    if (error is not null)
                    {
                        errorMessage.Set(error);
                        return;
                    }

                    groups.Add(new FetchedIssueGroup(null, issues));
                }
                else
                {
                    foreach (var assignee in assigneeFilters)
                    {
                        var (issues, error) = await githubService.SearchIssuesAsync(new IssueSearchRequest(
                            repo.Owner, repo.Name, query, assignee, labels));
                        if (error is not null)
                        {
                            errorMessage.Set(error);
                            return;
                        }

                        groups.Add(new FetchedIssueGroup(assignee, issues));
                    }
                }

                fetchedIssueGroups.Set(groups);
                resultsTruncated.Set(groups.Any(g => g.Issues.Count >= GithubService.MaxIssueLimit));
                var allIssueNumbers = groups
                    .SelectMany(g => g.Issues)
                    .Select(i => i.Number)
                    .ToHashSet();
                selectedIssueNumbers.Set(allIssueNumbers);

                var first = groups.SelectMany(g => g.Issues).FirstOrDefault();
                activeIssue.Set(first);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch issues");
                errorMessage.Set($"Failed to fetch issues: {ex.Message}");
                fetchedIssueGroups.Set(null);
            }
            finally
            {
                isFetching.Set(false);
            }
        }

        async Task ImportIssues(IReadOnlyList<GitHubIssue> issuesToImport)
        {
            if (issuesToImport.Count == 0) return;
            if (selectedRepo.Value is not { } repoName) return;
            var repo = repos.FirstOrDefault(r => r.DisplayName == repoName);
            if (repo is null) return;

            var distinctIssues = issuesToImport
                .DistinctBy(i => i.Number)
                .ToList();
            if (distinctIssues.Count == 0) return;

            isImporting.Set(true);
            try
            {
                var inboxPath = Path.Combine(config.TendrilHome, "Inbox");
                Directory.CreateDirectory(inboxPath);

                var projectName = GetProjectForRepo(githubService, repo.Owner, repo.Name);
                var importedCount = 0;

                foreach (var issue in distinctIssues)
                {
                    var safeName = SanitizeFileName(issue.Title);
                    var fileName = $"{issue.Number}-{safeName}.md";
                    var filePath = Path.Combine(inboxPath, fileName);

                    if (File.Exists(filePath)) continue;

                    var content = $"""
                                   ---
                                   project: {projectName}
                                   ---
                                   [GitHub Issue #{issue.Number}](https://github.com/{repo.Owner}/{repo.Name}/issues/{issue.Number})

                                   {issue.Body}
                                   """;

                    await FileHelper.WriteAllTextAsync(filePath, content);
                    importedCount++;
                }

                client.Toast($"Imported {importedCount} issue{(importedCount == 1 ? "" : "s")} to Inbox", "Import Complete");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Import failed");
                client.Toast($"Import failed: {ex.Message}", "Error");
            }
            finally
            {
                isImporting.Set(false);
            }
        }

        var selectedRepoConfig = repos.FirstOrDefault(r => r.DisplayName == selectedRepo.Value);

        var sidebar = new SidebarView(
            repos,
            selectedRepo,
            searchQuery,
            selectedAssignees,
            selectedLabels,
            filtersOpen,
            isFetching,
            errorMessage,
            resultsTruncated,
            fetchedIssueGroups,
            selectedIssueNumbers,
            activeIssue,
            FetchIssues
        );

        var content = new ContentView(
            activeIssue,
            allFetchedIssues,
            selectedIssueNumbers,
            selectedRepoConfig,
            isImporting,
            githubService,
            config,
            ImportIssues
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
