using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Git;

namespace Ivy.Tendril.Apps.Issues;

public class SidebarView(
    List<RepoConfig> repos,
    IState<string?> selectedRepo,
    IState<string> searchQuery,
    IState<string[]> selectedAssignees,
    IState<string[]> selectedLabels,
    IState<bool> filtersOpen,
    IState<bool> isFetching,
    IState<string?> errorMessage,
    IState<bool> resultsTruncated,
    IState<IReadOnlyList<FetchedIssueGroup>?> fetchedIssueGroups,
    IState<HashSet<int>> selectedIssueNumbers,
    IState<GitHubIssue?> activeIssue,
    Func<Task> onFetchIssues) : ViewBase
{
    public override object Build()
    {
        var githubService = UseService<IGithubService>();
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
                    return [];
                }
                var repo = githubService.GetRepos().FirstOrDefault(r => r.DisplayName == repoName);
                if (repo is null)
                {
                    assigneesError.Set(null);
                    return [];
                }
                var (assignees, error) = await githubService.GetAssigneesAsync(repo.Owner, repo.Name);
                assigneesError.Set(error);
                return assignees.ToArray();
            },
            initialValue: []
        );

        var labelsQuery = UseQuery<string[], string>(
            $"labels:{selectedRepo.Value ?? ""}",
            async (key, _) =>
            {
                var repoName = key.StartsWith("labels:") ? key["labels:".Length..] : key;
                if (string.IsNullOrEmpty(repoName))
                {
                    labelsError.Set(null);
                    return [];
                }
                var repo = githubService.GetRepos().FirstOrDefault(r => r.DisplayName == repoName);
                if (repo is null)
                {
                    labelsError.Set(null);
                    return [];
                }
                var (labels, error) = await githubService.GetLabelsAsync(repo.Owner, repo.Name);
                labelsError.Set(error);
                return labels.ToArray();
            },
            initialValue: []
        );

        object BuildHeader()
        {
            var repositoryOptions = repos.Select(r => r.DisplayName).ToArray();
            var hasRepo = !string.IsNullOrEmpty(selectedRepo.Value);
            var filtersLoading = hasRepo && (assigneesQuery.Loading || labelsQuery.Loading);

            var searchInput = searchQuery.ToSearchInput()
                .Placeholder("Search titles and descriptions")
                .Suffix(
                    new Button()
                        .Icon(filtersOpen.Value ? Icons.ChevronUp : Icons.ChevronDown)
                        .Ghost()
                        .OnClick(() => filtersOpen.Set(!filtersOpen.Value))
                );

            var header = Layout.Vertical().Gap(2)
                | selectedRepo.ToSelectInput(repositoryOptions.ToOptions())
                    .Placeholder("Select repository...")
                    .WithField().Label("Repository")
                | searchInput;

            if (filtersOpen.Value)
            {
                header |= Layout.Vertical().Gap(2)
                    | selectedAssignees.ToSelectInput((assigneesQuery.Value ?? []).ToOptions())
                        .Disabled(!hasRepo || filtersLoading)
                        .Placeholder(filtersLoading ? "Loading assignees..." : "Select assignees...")
                        .WithField().Label("Assignees")
                    | selectedLabels.ToSelectInput((labelsQuery.Value ?? []).ToOptions())
                        .Disabled(!hasRepo || filtersLoading)
                        .Placeholder(filtersLoading ? "Loading labels..." : "Select labels...")
                        .WithField().Label("Labels");

                if (assigneesError.Value is { } aErr)
                    header |= Text.Danger(aErr).Small();

                if (labelsError.Value is { } lErr)
                    header |= Text.Danger(lErr).Small();
            }

            header |= new Button("Fetch Issues").Outline().Loading(isFetching.Value)
                .Disabled(!hasRepo || isFetching.Value)
                .OnClick(async () => await onFetchIssues());

            if (errorMessage.Value is { } error)
                header |= Text.Danger(error).Small();

            if (resultsTruncated.Value)
                header |= Text.Muted($"Showing the first {GithubService.MaxIssueLimit} issues. Narrow the search, labels, or assignees to see the rest.").Small();

            return header;
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

        object? content;
        if (isFetching.Value)
        {
            content = Layout.Vertical().Gap(2).AlignContent(Align.Center)
                .Height(Size.Rem(18)).Width(Size.Full())
                | new Loading()
                | Text.Muted("Fetching issues from GitHub...");
        }
        else if (fetchedIssueGroups.Value is { } groups)
        {
            if (groups.All(g => g.Issues.Count == 0))
            {
                content = new NoResultsView();
            }
            else
            {
                var groupPanels = Layout.Vertical().Gap(4).Width(Size.Full());

                foreach (var group in groups)
                {
                    var groupIssues = group.Issues;
                    var groupSelectedCount = groupIssues.Count(i => selectedIssueNumbers.Value.Contains(i.Number));

                    var groupHeader = Layout.Horizontal().Gap(2).AlignContent(Align.SpaceBetween).Width(Size.Full())
                        | Text.Label(IssuesApp.FormatGroupHeader(group, groupSelectedCount))
                        | (Layout.Horizontal().Gap(1)
                            | new Button("All").Ghost().Small()
                                .Disabled(groupSelectedCount == groupIssues.Count || groupIssues.Count == 0)
                                .OnClick(() => SelectAllInGroup(groupIssues))
                            | new Button("None").Ghost().Small()
                                .Disabled(groupSelectedCount == 0)
                                .OnClick(() => SelectNoneInGroup(groupIssues)));

                    var issueRows = groupIssues.Select(issue =>
                    {
                        var isSelected = selectedIssueNumbers.Value.Contains(issue.Number);
                        var isCurrent = activeIssue.Value?.Number == issue.Number;

                        var titleBlock = Layout.Vertical().Gap(1).AlignContent(Align.Left).Width(Size.Grow())
                            | Text.Block($"#{issue.Number} {issue.Title}").NoWrap().Overflow(Overflow.Ellipsis);

                        if (issue.Labels.Length > 0)
                        {
                            titleBlock |= Layout.Horizontal().Gap(1).Wrap()
                                | issue.Labels.Select(l => new Badge(l).Variant(BadgeVariant.Outline).Small())
                                    .Cast<object>().ToArray();
                        }

                        var issueButton = new Button()
                            .Content(titleBlock)
                            .Variant(isCurrent ? ButtonVariant.Secondary : ButtonVariant.Ghost)
                            .Width(Size.Grow())
                            .OnClick(() => activeIssue.Set(issue));

                        var checkboxButton = new Button()
                            .Icon(isSelected ? Icons.Check : Icons.Square)
                            .Ghost().Small()
                            .Width(Size.Shrink())
                            .OnClick(() => ToggleIssueSelection(issue.Number));

                        return (object)(Layout.Horizontal().Gap(1).AlignContent(Align.Center).Width(Size.Full())
                            | checkboxButton
                            | issueButton);
                    }).ToArray();

                    var groupContent = groupIssues.Count > 0
                        ? (object)(Layout.Vertical().Gap(1).Width(Size.Full()) | issueRows)
                        : Text.Muted("No issues for this assignee.");

                    groupPanels |= Layout.Vertical().Gap(2).Width(Size.Full())
                        | groupHeader
                        | groupContent;
                }

                content = groupPanels;
            }
        }
        else
        {
            content = Layout.Vertical().AlignContent(Align.Center)
                .Height(Size.Rem(12)).Width(Size.Full())
                | Text.Muted("Choose a repository, set filters, and click Fetch Issues.");
        }

        return new HeaderLayout(BuildHeader(), content);
    }
}
