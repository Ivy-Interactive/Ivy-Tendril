using System.Text.RegularExpressions;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.AppShell.Dialogs;

public class ImportIssuesDialog(IState<bool> dialogOpen, IConfigService config) : ViewBase
{
    private sealed record FetchedIssueGroup(string? Assignee, IReadOnlyList<GitHubIssue> Issues);

    private readonly IConfigService _config = config;
    private readonly IState<bool> _dialogOpen = dialogOpen;

    public override object? Build()
    {
        var githubService = UseService<IGithubService>();
        var client = UseService<IClientProvider>();
        var logger = UseService<ILogger<ImportIssuesDialog>>();

        var selectedRepo = UseState<string?>(null);
        var searchQuery = UseState("");
        var selectedAssignees = UseState(Array.Empty<string>());
        var selectedLabels = UseState(Array.Empty<string>());
        var fetchedIssueGroups = UseState<IReadOnlyList<FetchedIssueGroup>?>(null);
        var selectedIssueNumbers = UseState<HashSet<int>>([]);
        var errorMessage = UseState<string?>(null);
        var isFetching = UseState(false);
        var isImporting = UseState(false);
        var assigneesError = UseState<string?>(null);
        var labelsError = UseState<string?>(null);

        var assigneesQuery = UseQuery<string[], string>(
            $"assignees:{selectedRepo.Value ?? ""}",
            async (key, _) =>
            {
                var repoName = key.StartsWith("assignees:") ? key["assignees:".Length..] : key;
                if (string.IsNullOrEmpty(repoName))
                {
                    assigneesError.Set(null);
                    return Array.Empty<string>();
                }
                var repo = githubService.GetRepos().FirstOrDefault(r => r.DisplayName == repoName);
                if (repo is null)
                {
                    assigneesError.Set(null);
                    return Array.Empty<string>();
                }
                var (assignees, error) = await githubService.GetAssigneesAsync(repo.Owner, repo.Name);
                assigneesError.Set(error);
                return assignees.ToArray();
            },
            initialValue: Array.Empty<string>()
        );

        var labelsQuery = UseQuery<string[], string>(
            $"labels:{selectedRepo.Value ?? ""}",
            async (key, _) =>
            {
                var repoName = key.StartsWith("labels:") ? key["labels:".Length..] : key;
                if (string.IsNullOrEmpty(repoName))
                {
                    labelsError.Set(null);
                    return Array.Empty<string>();
                }
                var repo = githubService.GetRepos().FirstOrDefault(r => r.DisplayName == repoName);
                if (repo is null)
                {
                    labelsError.Set(null);
                    return Array.Empty<string>();
                }
                var (labels, error) = await githubService.GetLabelsAsync(repo.Owner, repo.Name);
                labelsError.Set(error);
                return labels.ToArray();
            },
            initialValue: Array.Empty<string>()
        );

        UseEffect(() =>
        {
            fetchedIssueGroups.Set(null);
            selectedIssueNumbers.Set([]);
            errorMessage.Set(null);
            selectedAssignees.Set(Array.Empty<string>());
            selectedLabels.Set(Array.Empty<string>());
            assigneesError.Set(null);
            labelsError.Set(null);
        }, selectedRepo);

        if (!_dialogOpen.Value) return null;

        List<RepoConfig> repos;
        try
        {
            repos = githubService.GetRepos();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Exception loading repos");
            return new Dialog(
                _ => _dialogOpen.Set(false),
                new DialogHeader("Import Issues from GitHub"),
                new DialogBody(Text.Danger($"Failed to load repositories: {ex.Message}"))
            ).Width(Size.Rem(42));
        }

        if (repos.Count == 0)
        {
            return new Dialog(
                _ => _dialogOpen.Set(false),
                new DialogHeader("Import Issues from GitHub"),
                new DialogBody(Text.Danger(
                    "No GitHub repositories found. Check that your projects in config.yaml have valid git repositories with 'origin' remotes."))
            ).Width(Size.Rem(42));
        }

        var repositoryOptions = repos.Select(r => r.DisplayName).ToArray();
        var hasRepo = !string.IsNullOrEmpty(selectedRepo.Value);
        var filtersLoading = hasRepo && (assigneesQuery.Loading || labelsQuery.Loading);

        void ResetDialogState()
        {
            fetchedIssueGroups.Set(null);
            selectedIssueNumbers.Set([]);
            errorMessage.Set(null);
            searchQuery.Set("");
            selectedAssignees.Set(Array.Empty<string>());
            selectedLabels.Set(Array.Empty<string>());
            selectedRepo.Set(null);
        }

        void SelectAllInGroup(IReadOnlyList<GitHubIssue> issues)
        {
            var next = new HashSet<int>(selectedIssueNumbers.Value);
            foreach (var issue in issues)
                next.Add(issue.Number);
            selectedIssueNumbers.Set(next);
        }

        void SelectNoneInGroup(IReadOnlyList<GitHubIssue> issues)
        {
            var next = new HashSet<int>(selectedIssueNumbers.Value);
            foreach (var issue in issues)
                next.Remove(issue.Number);
            selectedIssueNumbers.Set(next);
        }

        void ToggleIssueSelection(int issueNumber)
        {
            var next = new HashSet<int>(selectedIssueNumbers.Value);
            if (!next.Remove(issueNumber))
                next.Add(issueNumber);
            selectedIssueNumbers.Set(next);
        }

        async Task FetchIssues()
        {
            if (selectedRepo.Value is not { } repoName) return;
            var repo = repos.FirstOrDefault(r => r.DisplayName == repoName);
            if (repo is null) return;

            isFetching.Set(true);
            errorMessage.Set(null);
            fetchedIssueGroups.Set(null);
            selectedIssueNumbers.Set([]);
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
                var allIssueNumbers = groups
                    .SelectMany(g => g.Issues)
                    .Select(i => i.Number)
                    .ToHashSet();
                selectedIssueNumbers.Set(allIssueNumbers);
            }
            catch (Exception ex)
            {
                errorMessage.Set($"Failed to fetch issues: {ex.Message}");
                fetchedIssueGroups.Set(null);
            }
            finally
            {
                isFetching.Set(false);
            }
        }

        async Task ImportSelected()
        {
            if (fetchedIssueGroups.Value is not { Count: > 0 } groups) return;
            var allIssues = groups.SelectMany(g => g.Issues).ToList();
            if (allIssues.Count == 0) return;
            if (selectedIssueNumbers.Value.Count == 0) return;
            if (selectedRepo.Value is not { } repoName) return;
            var repo = repos.FirstOrDefault(r => r.DisplayName == repoName);
            if (repo is null) return;

            var issuesToImport = allIssues
                .Where(i => selectedIssueNumbers.Value.Contains(i.Number))
                .DistinctBy(i => i.Number)
                .ToList();
            if (issuesToImport.Count == 0) return;

            isImporting.Set(true);
            try
            {
                var inboxPath = Path.Combine(_config.TendrilHome, "Inbox");
                Directory.CreateDirectory(inboxPath);

                var projectName = GetProjectForRepo(githubService, repo.Owner, repo.Name);
                var importedCount = 0;

                foreach (var issue in issuesToImport)
                {
                    var safeName = SanitizeFileName(issue.Title);
                    var fileName = $"{issue.Number}-{safeName}.md";
                    var filePath = Path.Combine(inboxPath, fileName);

                    if (File.Exists(filePath)) continue;

                    var issueUrl = $"https://github.com/{repo.Owner}/{repo.Name}/issues/{issue.Number}";
                    var content = $"""
                                   ---
                                   project: {projectName}
                                   sourceUrl: {issueUrl}
                                   sourceIdentifier: #{issue.Number}
                                   ---
                                   [GitHub Issue #{issue.Number}]({issueUrl})

                                   {issue.Body}
                                   """;

                    await File.WriteAllTextAsync(filePath, content);
                    importedCount++;
                }

                client.Toast($"Imported {importedCount} issue{(importedCount == 1 ? "" : "s")} to Inbox", "Import Complete");
                ResetDialogState();
                _dialogOpen.Set(false);
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

        object? issuesPanel;
        if (isFetching.Value)
        {
            issuesPanel = Layout.Vertical().Gap(2).AlignContent(Align.Center)
                .Height(Size.Rem(16)).Width(Size.Full())
                | new Loading()
                | Text.Muted("Fetching issues from GitHub...");
        }
        else if (fetchedIssueGroups.Value is { } groups)
        {
            if (groups.All(g => g.Issues.Count == 0))
            {
                issuesPanel = Layout.Vertical().AlignContent(Align.Center)
                    .Height(Size.Rem(10)).Width(Size.Full())
                    | Text.Muted("No issues found matching the filters.");
            }
            else
            {
                var repo = repos.FirstOrDefault(r => r.DisplayName == selectedRepo.Value);
                var scrollContent = Layout.Vertical().Gap(4).Width(Size.Full());

                foreach (var group in groups)
                {
                    var groupIssues = group.Issues;
                    var groupSelectedCount = groupIssues.Count(i => selectedIssueNumbers.Value.Contains(i.Number));
                    var issueRows = groupIssues.Select(issue =>
                    {
                        var issueUrl = repo != null
                            ? $"https://github.com/{repo.Owner}/{repo.Name}/issues/{issue.Number}"
                            : null;
                        return (object)new ImportIssueRowView(
                            issue,
                            selectedIssueNumbers,
                            group.Assignee,
                            issueUrl,
                            ToggleIssueSelection,
                            () => { if (issueUrl != null) client.OpenUrl(issueUrl); });
                    }).ToArray();

                    scrollContent |= Layout.Vertical().Gap(2).Width(Size.Full())
                        | Layout.Horizontal().Gap(2).AlignContent(Align.SpaceBetween).Width(Size.Full())
                            | Text.Label(FormatGroupHeader(group, groupSelectedCount))
                            | (Layout.Horizontal().Gap(1)
                                | new Button("All").Ghost().Small()
                                    .Disabled(groupSelectedCount == groupIssues.Count || groupIssues.Count == 0)
                                    .OnClick(() => SelectAllInGroup(groupIssues))
                                | new Button("None").Ghost().Small()
                                    .Disabled(groupSelectedCount == 0)
                                    .OnClick(() => SelectNoneInGroup(groupIssues)))
                        | (groupIssues.Count > 0
                            ? new List(issueRows).Width(Size.Full())
                            : Text.Muted("No issues for this assignee."));
                }

                issuesPanel = Layout.Vertical().Scroll(Scroll.Auto).Height(Size.Rem(22)).Width(Size.Full())
                    | scrollContent;
            }
        }
        else
        {
            issuesPanel = Layout.Vertical().AlignContent(Align.Center)
                .Height(Size.Rem(10)).Width(Size.Full())
                | Text.Muted("Choose a repository, set filters, and click Fetch Issues.");
        }

        var selectedForImport = selectedIssueNumbers.Value.Count;

        return new Dialog(
            _ =>
            {
                ResetDialogState();
                _dialogOpen.Set(false);
            },
            new DialogHeader("Import Issues from GitHub"),
            new DialogBody(
                Layout.Vertical().Gap(3)
                | selectedRepo.ToSelectInput(repositoryOptions.ToOptions())
                    .Placeholder("Select repository...")
                    .WithField().Label("Repository").Required()
                | searchQuery.ToTextInput().Placeholder("Search titles and descriptions").WithField().Label("Search")
                | selectedAssignees.ToSelectInput((assigneesQuery.Value ?? Array.Empty<string>()).ToOptions())
                    .Disabled(!hasRepo || filtersLoading)
                    .Placeholder(filtersLoading ? "Loading assignees..." : "Select assignees...")
                    .WithField().Label("Assignees")
                | selectedLabels.ToSelectInput((labelsQuery.Value ?? Array.Empty<string>()).ToOptions())
                    .Disabled(!hasRepo || filtersLoading)
                    .Placeholder(filtersLoading ? "Loading labels..." : "Select labels...")
                    .WithField().Label("Labels")
                | (assigneesError.Value is { } assigneeErr
                    ? Text.Danger(assigneeErr).Small()
                    : null)
                | (labelsError.Value is { } labelErr
                    ? Text.Danger(labelErr).Small()
                    : null)
                | new Button("Fetch Issues").Outline().Loading(isFetching.Value)
                    .Disabled(!hasRepo || isFetching.Value)
                    .OnClick(async () => await FetchIssues())
                | (errorMessage.Value is { } error
                    ? Text.Danger(error).Small()
                    : null)
                | issuesPanel
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() =>
                {
                    ResetDialogState();
                    _dialogOpen.Set(false);
                }),
                new Button(selectedForImport > 0
                        ? $"Import Selected ({selectedForImport})"
                        : "Import Selected")
                    .Primary()
                    .Loading(isImporting.Value)
                    .Disabled(selectedForImport == 0
                        || fetchedIssueGroups.Value is null
                        || !fetchedIssueGroups.Value.SelectMany(g => g.Issues).Any())
                    .OnClick(async () => await ImportSelected())
            )
        ).Width(Size.Rem(42));
    }

    private string GetProjectForRepo(IGithubService githubService, string owner, string repo)
    {
        var repoPath = $"{owner}/{repo}";
        var matchingProjects = _config.Settings.Projects
            .Where(p => p.RepoPaths.Any(path =>
                githubService.GetRepoConfigFromPathCached(path)?.FullName
                    .Equals(repoPath, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

        return matchingProjects.Count == 1 ? matchingProjects[0].Name : "Auto";
    }

    internal static string SanitizeFileName(string title)
    {
        var sanitized = Regex.Replace(title, @"[^a-zA-Z0-9\s-]", "");
        sanitized = Regex.Replace(sanitized, @"\s+", "-");
        sanitized = sanitized.Trim('-').ToLowerInvariant();
        return sanitized.Length > 60 ? sanitized[..60].TrimEnd('-') : sanitized;
    }

    private static string TruncateBody(string? body, int maxLength = 500)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";
        var trimmed = body.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength] + "…";
    }

    private static string FormatGroupHeader(FetchedIssueGroup group, int selectedCount)
    {
        var count = group.Issues.Count;
        var issueLabel = count == 1 ? "issue" : "issues";
        if (group.Assignee is { } assignee)
            return $"Found {count} {issueLabel} for {assignee} · {selectedCount} selected";

        return $"Found {count} {issueLabel} · {selectedCount} selected";
    }

    private static string[] GetIssueAssignees(GitHubIssue issue) =>
        issue.Assignees.Where(a => !string.IsNullOrWhiteSpace(a)).ToArray();

    private static bool ShouldShowAssignees(GitHubIssue issue, string? assigneeFilter)
    {
        var assignees = GetIssueAssignees(issue);
        if (assignees.Length == 0) return false;
        if (string.IsNullOrWhiteSpace(assigneeFilter)) return true;

        return assignees.Length > 1
            || !assignees[0].Equals(assigneeFilter, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ImportIssueRowView(
        GitHubIssue issue,
        IState<HashSet<int>> selectedNumbers,
        string? assigneeFilter,
        string? issueUrl,
        Action<int> onToggle,
        Action onOpenUrl) : ViewBase
    {
        public override object? Build()
        {
            var isSelected = selectedNumbers.Value.Contains(issue.Number);

            var bodyContent = Layout.Vertical().Gap(2);

            bodyContent |= string.IsNullOrWhiteSpace(issue.Body)
                ? Text.Muted("No description provided.")
                : Text.Block(TruncateBody(issue.Body));

            if (issue.Labels.Length > 0)
            {
                bodyContent |= Layout.Horizontal().Gap(2).AlignContent(Align.TopLeft).Wrap()
                    | Text.Muted("Labels:")
                    | issue.Labels.Select(label => new Badge(label).Variant(BadgeVariant.Outline).Small())
                        .Cast<object>().ToArray();
            }

            if (ShouldShowAssignees(issue, assigneeFilter))
            {
                bodyContent |= Layout.Horizontal().Gap(2).AlignContent(Align.TopLeft).Wrap()
                    | Text.Muted("Assigned to:")
                    | Text.Block(string.Join(", ", GetIssueAssignees(issue)));
            }

            return Layout.Horizontal().Gap(2).AlignContent(Align.Center).Width(Size.Full())
                | new Button()
                    .Icon(isSelected ? Icons.Check : Icons.Square)
                    .Ghost().Small()
                    .Width(Size.Shrink())
                    .OnClick(() => onToggle(issue.Number))
                | new Expandable($"#{issue.Number} {issue.Title}", bodyContent)
                    .Width(Size.Grow().Min(Size.Px(0)))
                | (issueUrl != null
                    ? new Button().Icon(Icons.ExternalLink).Ghost().Small()
                        .Width(Size.Shrink())
                        .Tooltip("Open on GitHub")
                        .OnClick(onOpenUrl)
                    : null);
        }
    }
}
