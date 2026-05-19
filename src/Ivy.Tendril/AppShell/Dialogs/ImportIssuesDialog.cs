using System.Text.RegularExpressions;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.AppShell.Dialogs;

public class ImportIssuesDialog(IState<bool> dialogOpen, IConfigService config) : ViewBase
{
    private readonly IConfigService _config = config;
    private readonly IState<bool> _dialogOpen = dialogOpen;

    public override object? Build()
    {
        var githubService = UseService<IGithubService>();
        var client = UseService<IClientProvider>();
        var logger = UseService<ILogger<ImportIssuesDialog>>();

        var selectedRepo = UseState<string?>(null);
        var searchQuery = UseState("");
        var selectedAssignee = UseState<string?>(null);
        var selectedLabels = UseState(Array.Empty<string>());
        var fetchedIssues = UseState<List<GitHubIssue>?>(null);
        var selectedIssueNumbers = UseState<HashSet<int>>([]);
        var errorMessage = UseState<string?>(null);
        var isFetching = UseState(false);
        var isImporting = UseState(false);
        var assigneesError = UseState<string?>(null);
        var labelsError = UseState<string?>(null);
        var reposState = UseState<List<RepoConfig>?>(null);
        var reposError = UseState<string?>(null);

        var assigneesQuery = UseQuery<string[], string>(
            $"assignees:{selectedRepo.Value ?? ""}",
            async (key, _) =>
            {
                var repoName = key.StartsWith("assignees:") ? key.Substring("assignees:".Length) : key;

                if (string.IsNullOrEmpty(repoName))
                {
                    assigneesError.Set(null);
                    return Array.Empty<string>();
                }
                var repos = githubService.GetRepos();
                var repo = repos.FirstOrDefault(r => r.DisplayName == repoName);
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
                var repoName = key.StartsWith("labels:") ? key.Substring("labels:".Length) : key;

                if (string.IsNullOrEmpty(repoName))
                {
                    labelsError.Set(null);
                    return Array.Empty<string>();
                }
                var repos = githubService.GetRepos();
                var repo = repos.FirstOrDefault(r => r.DisplayName == repoName);
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
            fetchedIssues.Set(null);
            selectedIssueNumbers.Set([]);
            errorMessage.Set(null);
            selectedAssignee.Set(null);
            selectedLabels.Set(Array.Empty<string>());
            assigneesError.Set(null);
            labelsError.Set(null);
        }, selectedRepo);

        UseEffect(() =>
        {
            if (!_dialogOpen.Value)
            {
                reposState.Set(null);
                reposError.Set(null);
                return;
            }

            try
            {
                var repos = githubService.GetRepos();
                if (repos.Count == 0)
                {
                    reposError.Set("No GitHub repositories found. Check that your projects in config.yaml have valid git repositories with 'origin' remotes.");
                }
                else
                {
                    reposState.Set(repos);
                }
            }
            catch (Exception ex)
            {
                reposError.Set($"Failed to load repositories: {ex.Message}");
                logger.LogWarning(ex, "Exception loading repos");
            }
        }, _dialogOpen);

        if (!_dialogOpen.Value) return null;

        if (reposState.Value is null && reposError.Value is null)
        {
            return new Dialog(
                _ => _dialogOpen.Set(false),
                new DialogHeader("Import Issues from GitHub"),
                new DialogBody(
                    Layout.Vertical().Gap(3).AlignContent(Align.Center)
                    | Text.Muted("Loading repositories...")
                    | new Loading()
                )
            ).Width(Size.Rem(42));
        }

        if (reposError.Value is { } repoErr)
        {
            return new Dialog(
                _ =>
                {
                    reposError.Set(null);
                    _dialogOpen.Set(false);
                },
                new DialogHeader("Import Issues from GitHub"),
                new DialogBody(Text.Danger(repoErr))
            ).Width(Size.Rem(42));
        }

        var repos = reposState.Value!;
        var repositoryOptions = repos.Select(r => r.DisplayName).ToArray();
        var hasRepo = !string.IsNullOrEmpty(selectedRepo.Value);
        var filtersLoading = hasRepo && (assigneesQuery.Loading || labelsQuery.Loading);

        void ResetDialogState()
        {
            fetchedIssues.Set(null);
            selectedIssueNumbers.Set([]);
            errorMessage.Set(null);
            searchQuery.Set("");
            selectedAssignee.Set(null);
            selectedLabels.Set(Array.Empty<string>());
            selectedRepo.Set(null);
        }

        void SelectAllIssues()
        {
            if (fetchedIssues.Value is not { } issues) return;
            selectedIssueNumbers.Set(issues.Select(i => i.Number).ToHashSet());
        }

        void SelectNoIssues() => selectedIssueNumbers.Set([]);

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
            fetchedIssues.Set(null);
            selectedIssueNumbers.Set([]);
            try
            {
                var labels = selectedLabels.Value.Length > 0 ? selectedLabels.Value : null;
                var (issues, error) = await githubService.SearchIssuesAsync(new IssueSearchRequest(
                    repo.Owner, repo.Name,
                    string.IsNullOrWhiteSpace(searchQuery.Value) ? null : searchQuery.Value,
                    selectedAssignee.Value,
                    labels));

                if (error is not null)
                {
                    errorMessage.Set(error);
                    fetchedIssues.Set(null);
                }
                else
                {
                    fetchedIssues.Set(issues);
                    selectedIssueNumbers.Set(issues.Select(i => i.Number).ToHashSet());
                }
            }
            catch (Exception ex)
            {
                errorMessage.Set($"Failed to fetch issues: {ex.Message}");
                fetchedIssues.Set(null);
            }
            finally
            {
                isFetching.Set(false);
            }
        }

        async Task ImportSelected()
        {
            if (fetchedIssues.Value is not { Count: > 0 } issues) return;
            if (selectedIssueNumbers.Value.Count == 0) return;
            if (selectedRepo.Value is not { } repoName) return;
            var repo = repos.FirstOrDefault(r => r.DisplayName == repoName);
            if (repo is null) return;

            var issuesToImport = issues.Where(i => selectedIssueNumbers.Value.Contains(i.Number)).ToList();
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

                    var content = $"""
                                   ---
                                   project: {projectName}
                                   ---
                                   [GitHub Issue #{issue.Number}](https://github.com/{repo.Owner}/{repo.Name}/issues/{issue.Number})

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
        else if (fetchedIssues.Value is { } issues)
        {
            if (issues.Count == 0)
            {
                issuesPanel = Layout.Vertical().AlignContent(Align.Center)
                    .Height(Size.Rem(10)).Width(Size.Full())
                    | Text.Muted("No issues found matching the filters.");
            }
            else
            {
                var repo = repos.FirstOrDefault(r => r.DisplayName == selectedRepo.Value);
                var selectedCount = selectedIssueNumbers.Value.Count;
                var issueRows = issues.Select(issue =>
                {
                    var issueUrl = repo != null
                        ? $"https://github.com/{repo.Owner}/{repo.Name}/issues/{issue.Number}"
                        : null;
                    return (object)new ImportIssueRowView(
                        issue,
                        selectedIssueNumbers,
                        issueUrl,
                        ToggleIssueSelection,
                        () => { if (issueUrl != null) client.OpenUrl(issueUrl); });
                }).ToArray();

                issuesPanel = Layout.Vertical().Gap(2).Width(Size.Full())
                    | Layout.Horizontal().Gap(2).AlignContent(Align.SpaceBetween).Width(Size.Full())
                        | Text.Label($"Found {issues.Count} issue{(issues.Count == 1 ? "" : "s")} · {selectedCount} selected")
                        | (Layout.Horizontal().Gap(1)
                            | new Button("All").Ghost().Small().Disabled(selectedCount == issues.Count)
                                .OnClick(SelectAllIssues)
                            | new Button("None").Ghost().Small().Disabled(selectedCount == 0)
                                .OnClick(SelectNoIssues))
                    | (Layout.Vertical().Scroll(Scroll.Auto).Height(Size.Rem(22)).Width(Size.Full())
                        | new List(issueRows).Width(Size.Full()));
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
                    .WithField().Label("Repository").Required()
                | searchQuery.ToTextInput().Placeholder("Search titles and descriptions").WithField().Label("Search")
                | selectedAssignee.ToSelectInput(assigneesQuery.Value.ToOptions())
                    .Nullable()
                    .Disabled(!hasRepo || filtersLoading)
                    .Placeholder(filtersLoading ? "Loading assignees..." : "Any assignee")
                    .WithField().Label("Assignee")
                | selectedLabels.ToSelectInput(labelsQuery.Value.ToOptions())
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
                    .Disabled(selectedForImport == 0 || fetchedIssues.Value is not { Count: > 0 })
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

    private sealed class ImportIssueRowView(
        GitHubIssue issue,
        IState<HashSet<int>> selectedNumbers,
        string? issueUrl,
        Action<int> onToggle,
        Action onOpenUrl) : ViewBase
    {
        public override object? Build()
        {
            var isSelected = selectedNumbers.Value.Contains(issue.Number);

            object? labelsRow = null;
            if (issue.Labels.Length > 0)
            {
                labelsRow = Layout.Horizontal().Gap(1).Wrap()
                    | issue.Labels.Select(label => new Badge(label).Variant(BadgeVariant.Outline).Small())
                        .Cast<object>().ToArray();
            }

            var bodyContent = Layout.Vertical().Gap(2)
                | (string.IsNullOrWhiteSpace(issue.Body)
                    ? Text.Muted("No description provided.")
                    : Text.Block(TruncateBody(issue.Body)))
                | labelsRow;

            return Layout.Horizontal().Gap(2).AlignContent(Align.Center).Width(Size.Full())
                | new Button()
                    .Icon(isSelected ? Icons.Check : Icons.Square)
                    .Ghost().Small()
                    .Width(Size.Shrink())
                    .OnClick(() => onToggle(issue.Number))
                | new Expandable($"#{issue.Number} — {issue.Title}", bodyContent)
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
