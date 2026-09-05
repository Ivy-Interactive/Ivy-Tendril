using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Apps.Views.Sheets;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Git;

namespace Ivy.Tendril.Apps.Inbox;

public class ContentView(
    IState<InboxCategory> selectedCategory,
    IState<string?> selectedProject,
    IReadOnlyList<ProjectConfig> projects,
    IState<string> searchQuery,
    IState<string[]> selectedAssignees,
    IState<string[]> selectedLabels,
    IReadOnlyList<string> availableAssignees,
    IReadOnlyList<string> availableLabels,
    IState<HashSet<int>> selectedIssueNumbers,
    IReadOnlyList<GitHubIssue> myIssues,
    IReadOnlyList<GitHubReviewItem> reviewRequests,
    IReadOnlyList<GitHubIssue> projectIssues,
    bool isFetching,
    string? errorMessage,
    IState<bool> isImporting,
    IConfigService config,
    IGithubService githubService,
    Func<Task> onRefresh,
    Func<IReadOnlyList<GitHubIssue>, Task> onFireOffIssues) : ViewBase
{
    public override object Build()
    {
        var client = UseService<IClientProvider>();
        var openFile = UseState<string?>(null);

        var (issueSheet, showIssueSheet) = UseTrigger<GitHubIssue>((isOpen, issue) =>
        {
            if (!isOpen.Value || issue == null) return null;

            var issueUrl = issue.Url ?? (issue.Repository != null
                ? $"https://github.com/{issue.Repository}/issues/{issue.Number}"
                : null);

            var sheetHeader = Layout.Vertical().Width(Size.Full())
                | (Layout.Horizontal().AlignContent(Align.SpaceBetween).Width(Size.Full())
                    | (Layout.Horizontal().AlignContent(Align.Left).Wrap()
                        | (!string.IsNullOrEmpty(issue.Repository) ? new Badge(issue.Repository).Variant(BadgeVariant.Secondary).Small() : null)
                        | (issue.Assignees.Length > 0 ? Text.Muted($"Assigned: {string.Join(", ", issue.Assignees.Where(a => !string.IsNullOrWhiteSpace(a)))}").Small() : null))
                    | (Layout.Horizontal().AlignContent(Align.Right)
                        | (issueUrl != null
                            ? new Button("GitHub")
                                .Icon(Icons.ExternalLink)
                                .Ghost().Small()
                                .OnClick(() => client.OpenUrl(issueUrl))
                            : null)
                        | new Button("Fire off in Tendril")
                            .Icon(Icons.Zap)
                            .Primary().Small()
                            .Loading(isImporting.Value)
                            .OnClick(async () =>
                            {
                                await onFireOffIssues([issue]);
                                isOpen.Set(false);
                            })))
                | (issue.Labels.Length > 0
                    ? (Layout.Horizontal().AlignContent(Align.Left).Wrap()
                        | issue.Labels.Select(l => (object)new Badge(l).Variant(BadgeVariant.Outline).Small()).ToArray())
                    : null);

            var sheetBody = string.IsNullOrWhiteSpace(issue.Body)
                ? (object)Text.Muted("No description provided.")
                : new Markdown(MarkdownHelper.PrepareForDisplay(issue.Body, config))
                    .Article()
                    .DangerouslyAllowLocalFiles()
                    .OnLinkClick(FileSheet.CreateLinkClickHandler(openFile));

            var sheetContent = Layout.Vertical().Width(Size.Full()).Scroll(Scroll.Auto)
                | sheetHeader
                | new Separator()
                | sheetBody;

            var sheet = new Sheet(
                () => isOpen.Set(false),
                sheetContent,
                $"#{issue.Number} {issue.Title}"
            ).Width(UxHelper.SheetWidth).Resizable();

            return sheet;
        });

        var (reviewSheet, showReviewSheet) = UseTrigger<GitHubReviewItem>((isOpen, review) =>
        {
            if (!isOpen.Value || review == null) return null;

            var sheetHeader = Layout.Vertical().Width(Size.Full())
                | (Layout.Horizontal().AlignContent(Align.SpaceBetween).Width(Size.Full())
                    | (Layout.Horizontal().AlignContent(Align.Left).Wrap()
                        | new Badge(review.Repository).Variant(BadgeVariant.Secondary).Small()
                        | (!string.IsNullOrEmpty(review.Branch) ? new Badge(review.Branch).Variant(BadgeVariant.Outline).Small() : null)
                        | (review.Assignees.Length > 0 ? Text.Muted($"Assigned: {string.Join(", ", review.Assignees.Where(a => !string.IsNullOrWhiteSpace(a)))}").Small() : null))
                    | (Layout.Horizontal().AlignContent(Align.Right)
                        | new Button("Open on GitHub")
                            .Icon(Icons.ExternalLink)
                            .Primary().Small()
                            .OnClick(() => client.OpenUrl(review.Url))));

            var sheetBody = string.IsNullOrWhiteSpace(review.Body)
                ? (object)Text.Muted("No description provided.")
                : new Markdown(MarkdownHelper.PrepareForDisplay(review.Body, config))
                    .Article()
                    .DangerouslyAllowLocalFiles()
                    .OnLinkClick(FileSheet.CreateLinkClickHandler(openFile));

            var sheetContent = Layout.Vertical().Width(Size.Full()).Scroll(Scroll.Auto)
                | sheetHeader
                | new Separator()
                | sheetBody;

            var sheet = new Sheet(
                () => isOpen.Set(false),
                sheetContent,
                $"#{review.Number} {review.Title}"
            ).Width(UxHelper.SheetWidth).Resizable();

            return sheet;
        });

        object mainView;

        if (selectedCategory.Value == InboxCategory.Reviews)
        {
            mainView = BuildReviewsView(client, showReviewSheet);
        }
        else if (selectedCategory.Value == InboxCategory.MyIssues)
        {
            mainView = BuildIssuesView(
                title: "My Issues",
                subtitle: "Issues assigned to you across GitHub repositories",
                allIssues: myIssues,
                showRepoBadge: true,
                showProjectFilters: false,
                client: client,
                showIssueSheet: showIssueSheet
            );
        }
        else // Project
        {
            var currentProj = projects.FirstOrDefault(p =>
                string.Equals(p.Name, selectedProject.Value, StringComparison.OrdinalIgnoreCase));
            var projName = currentProj?.Name ?? selectedProject.Value ?? "Project";

            mainView = BuildIssuesView(
                title: $"{projName} Issues",
                subtitle: $"Browse and fire off issues for {projName}",
                allIssues: projectIssues,
                showRepoBadge: false,
                showProjectFilters: true,
                client: client,
                showIssueSheet: showIssueSheet
            );
        }

        return new Fragment(mainView, issueSheet, reviewSheet, new FileSheet(openFile, config));
    }

    private object BuildReviewsView(IClientProvider client, Action<GitHubReviewItem> showReviewSheet)
    {
        var filteredReviews = reviewRequests;
        if (!string.IsNullOrWhiteSpace(searchQuery.Value))
        {
            var q = searchQuery.Value.Trim();
            filteredReviews = filteredReviews.Where(r =>
                r.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.Number.ToString().Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.Repository.Contains(q, StringComparison.OrdinalIgnoreCase)
            ).ToList();
        }

        var searchInput = searchQuery.ToSearchInput()
            .Placeholder("Search reviews by title, #number, or repo...");

        var refreshButton = new Button()
            .Icon(Icons.RefreshCw)
            .Ghost()
            .Tooltip("Refresh")
            .Loading(isFetching)
            .OnClick(async () => await onRefresh());

        var headerToolbar = Layout.Horizontal().AlignContent(Align.SpaceBetween).Width(Size.Full())
            | (Layout.Vertical().AlignContent(Align.Left)
                | Text.H3("Reviews").Bold()
                | Text.Muted("Pull requests requesting your review on GitHub"))
            | (Layout.Horizontal().AlignContent(Align.Right)
                | searchInput
                | refreshButton);

        if (isFetching && reviewRequests.Count == 0)
        {
            return new HeaderLayout(
                headerToolbar,
                Layout.Vertical().AlignContent(Align.Center).Height(Size.Full())
                    | new Loading()
                    | Text.Muted("Fetching review requests from GitHub...")
            );
        }

        if (errorMessage != null)
        {
            return new HeaderLayout(
                headerToolbar,
                Layout.Vertical().AlignContent(Align.Center).Height(Size.Full())
                    | Text.Danger(errorMessage)
                    | new Button("Retry").Outline().OnClick(async () => await onRefresh())
            );
        }

        if (filteredReviews.Count == 0)
        {
            return new HeaderLayout(
                headerToolbar,
                new NoContentView("All Caught Up!", "No pull requests currently require your review.")
            );
        }

        var headerRow = new TableRow(
            new TableCell(Text.Literal("Pull Request")).IsHeader(),
            new TableCell(Text.Literal("Repository")).Width(Size.Px(180)).IsHeader(),
            new TableCell(Text.Literal("Branch")).Width(Size.Px(160)).IsHeader(),
            new TableCell(Text.Literal("Updated")).Width(Size.Px(100)).IsHeader(),
            new TableCell(Text.Literal("Actions")).Width(Size.Px(140)).AlignContent(Align.Right).IsHeader()
        ).IsHeader();

        var dataRows = filteredReviews.Select(pr =>
        {
            var titleCell = new TableCell(
                new Button($"#{pr.Number} {pr.Title}")
                    .Link()
                    .OnClick(() => showReviewSheet(pr))
            );

            var repoCell = new TableCell(new Badge(pr.Repository).Variant(BadgeVariant.Secondary).Small()).Width(Size.Px(180));
            var branchCell = new TableCell(!string.IsNullOrEmpty(pr.Branch) ? new Badge(pr.Branch).Variant(BadgeVariant.Outline).Small() : null).Width(Size.Px(160));
            var dateCell = new TableCell(pr.UpdatedAt.HasValue ? Text.Muted(pr.UpdatedAt.Value.ToString("M/d")).Small() : null).Width(Size.Px(100));

            var actionsCell = new TableCell(
                Layout.Horizontal().AlignContent(Align.Right)
                    | new Button("Review")
                        .Icon(Icons.ExternalLink)
                        .Outline().Small()
                        .OnClick(() => client.OpenUrl(pr.Url))
            ).Width(Size.Px(140)).AlignContent(Align.Right);

            return new TableRow(titleCell, repoCell, branchCell, dateCell, actionsCell);
        }).ToArray();

        var table = new Table(new[] { headerRow }.Concat(dataRows).ToArray()).Width(Size.Full());

        return new HeaderLayout(headerToolbar, table);
    }

    private object BuildIssuesView(
        string title,
        string subtitle,
        IReadOnlyList<GitHubIssue> allIssues,
        bool showRepoBadge,
        bool showProjectFilters,
        IClientProvider client,
        Action<GitHubIssue> showIssueSheet)
    {
        var filteredIssues = allIssues;

        if (!string.IsNullOrWhiteSpace(searchQuery.Value))
        {
            var q = searchQuery.Value.Trim();
            filteredIssues = filteredIssues.Where(i =>
                i.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.Number.ToString().Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (i.Body != null && i.Body.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                (i.Repository != null && i.Repository.Contains(q, StringComparison.OrdinalIgnoreCase))
            ).ToList();
        }

        if (selectedAssignees.Value.Length > 0)
        {
            var filterAssignees = selectedAssignees.Value.ToHashSet(StringComparer.OrdinalIgnoreCase);
            filteredIssues = filteredIssues.Where(i =>
                i.Assignees.Any(a => filterAssignees.Contains(a))
            ).ToList();
        }

        if (selectedLabels.Value.Length > 0)
        {
            var filterLabels = selectedLabels.Value.ToHashSet(StringComparer.OrdinalIgnoreCase);
            filteredIssues = filteredIssues.Where(i =>
                i.Labels.Any(l => filterLabels.Contains(l))
            ).ToList();
        }

        var searchInput = searchQuery.ToSearchInput()
            .Placeholder("Search issues by title, #number, or description...");

        var refreshButton = new Button()
            .Icon(Icons.RefreshCw)
            .Ghost()
            .Tooltip("Refresh")
            .Loading(isFetching)
            .OnClick(async () => await onRefresh());

        var filterBar = Layout.Horizontal().AlignContent(Align.Right).Wrap()
            | searchInput;

        if (showProjectFilters && availableAssignees.Count > 0)
        {
            filterBar |= selectedAssignees.ToSelectInput(availableAssignees.ToOptions())
                .Placeholder("Assignees...");
        }

        if (showProjectFilters && availableLabels.Count > 0)
        {
            filterBar |= selectedLabels.ToSelectInput(availableLabels.ToOptions())
                .Placeholder("Labels...");
        }

        filterBar |= refreshButton;

        var headerTop = Layout.Horizontal().AlignContent(Align.SpaceBetween).Width(Size.Full())
            | (Layout.Vertical().AlignContent(Align.Left)
                | Text.H3(title).Bold()
                | Text.Muted(subtitle))
            | filterBar;

        if (isFetching && allIssues.Count == 0)
        {
            return new HeaderLayout(
                headerTop,
                Layout.Vertical().AlignContent(Align.Center).Height(Size.Full())
                    | new Loading()
                    | Text.Muted("Loading issues from GitHub...")
            );
        }

        if (errorMessage != null)
        {
            return new HeaderLayout(
                headerTop,
                Layout.Vertical().AlignContent(Align.Center).Height(Size.Full())
                    | Text.Danger(errorMessage)
                    | new Button("Retry").Outline().OnClick(async () => await onRefresh())
            );
        }

        if (filteredIssues.Count == 0)
        {
            return new HeaderLayout(
                headerTop,
                new NoContentView("No Issues Found", "No issues match the selected view or search filters.")
            );
        }

        var selectedCount = selectedIssueNumbers.Value.Count;
        var selectedIssuesList = filteredIssues
            .Where(i => selectedIssueNumbers.Value.Contains(i.Number))
            .ToList();

        void SelectAll()
        {
            var next = new HashSet<int>(selectedIssueNumbers.Value);
            foreach (var i in filteredIssues) next.Add(i.Number);
            selectedIssueNumbers.Set(next);
        }

        void DeselectAll()
        {
            var next = new HashSet<int>(selectedIssueNumbers.Value);
            foreach (var i in filteredIssues) next.Remove(i.Number);
            selectedIssueNumbers.Set(next);
        }

        var allSelected = filteredIssues.Count > 0 && selectedCount == filteredIssues.Count;
        var anySelected = selectedCount > 0;

        var batchBar = Layout.Horizontal().AlignContent(Align.SpaceBetween).Width(Size.Full())
            | (Layout.Horizontal().AlignContent(Align.Left)
                | new Button("Select All").Ghost().Small().OnClick(SelectAll)
                | new Button("Deselect All").Ghost().Small().Disabled(selectedCount == 0).OnClick(DeselectAll)
                | Text.Muted($"{selectedCount} of {filteredIssues.Count} selected").Small())
            | new Button(selectedCount > 0 ? $"Fire off in Tendril ({selectedCount})" : "Fire off in Tendril")
                .Icon(Icons.Zap)
                .Primary()
                .Disabled(selectedCount == 0 || isImporting.Value)
                .Loading(isImporting.Value)
                .OnClick(async () => await onFireOffIssues(selectedIssuesList));

        var fullHeader = Layout.Vertical().Width(Size.Full())
            | headerTop
            | new Separator()
            | batchBar;

        var headerRow = new TableRow(
            new TableCell(
                new Button()
                    .Icon(allSelected ? Icons.SquareCheck : (anySelected ? Icons.SquareMinus : Icons.Square))
                    .Ghost().Small()
                    .OnClick(() =>
                    {
                        if (allSelected) DeselectAll();
                        else SelectAll();
                    })
            ).Width(Size.Px(44)).AlignContent(Align.Center).IsHeader(),
            new TableCell(Text.Literal("Issue")).IsHeader(),
            new TableCell(Text.Literal("Repository")).Width(Size.Px(180)).IsHeader(),
            new TableCell(Text.Literal("Labels")).Width(Size.Px(200)).IsHeader(),
            new TableCell(Text.Literal("Assignees")).Width(Size.Px(160)).IsHeader(),
            new TableCell(Text.Literal("Actions")).Width(Size.Px(100)).AlignContent(Align.Right).IsHeader()
        ).IsHeader();

        var dataRows = filteredIssues.Select(issue =>
        {
            var isChecked = selectedIssueNumbers.Value.Contains(issue.Number);
            var issueUrl = issue.Url ?? (issue.Repository != null
                ? $"https://github.com/{issue.Repository}/issues/{issue.Number}"
                : null);

            var checkboxCell = new TableCell(
                new Button()
                    .Icon(isChecked ? Icons.SquareCheck : Icons.Square)
                    .Ghost().Small()
                    .OnClick(() =>
                    {
                        var next = new HashSet<int>(selectedIssueNumbers.Value);
                        if (!next.Remove(issue.Number)) next.Add(issue.Number);
                        selectedIssueNumbers.Set(next);
                    })
            ).Width(Size.Px(44)).AlignContent(Align.Center);

            var titleCell = new TableCell(
                new Button($"#{issue.Number} {issue.Title}")
                    .Link()
                    .OnClick(() => showIssueSheet(issue))
            );

            var repoCell = new TableCell(
                (showRepoBadge || !string.IsNullOrEmpty(issue.Repository))
                    ? (!string.IsNullOrEmpty(issue.Repository) ? new Badge(issue.Repository).Variant(BadgeVariant.Secondary).Small() : null)
                    : null
            ).Width(Size.Px(180));

            var labelsContent = issue.Labels.Length > 0
                ? (object)(Layout.Horizontal().AlignContent(Align.Left).Wrap()
                    | issue.Labels.Take(3).Select(l => (object)new Badge(l).Variant(BadgeVariant.Outline).Small()).ToArray()
                    | (issue.Labels.Length > 3 ? Text.Muted($"+{issue.Labels.Length - 3}").Small() : null))
                : null;
            var labelsCell = new TableCell(labelsContent).Width(Size.Px(200));

            var assigneesContent = issue.Assignees.Length > 0
                ? Text.Muted(string.Join(", ", issue.Assignees.Where(a => !string.IsNullOrWhiteSpace(a)))).Small()
                : null;
            var assigneesCell = new TableCell(assigneesContent).Width(Size.Px(160));

            var actionsContent = Layout.Horizontal().AlignContent(Align.Right)
                | new Button()
                    .Icon(Icons.Zap)
                    .Ghost().Small()
                    .Tooltip("Fire off in Tendril")
                    .Loading(isImporting.Value)
                    .OnClick(async () => await onFireOffIssues([issue]))
                | (issueUrl != null
                    ? new Button()
                        .Icon(Icons.ExternalLink)
                        .Ghost().Small()
                        .Tooltip("Open on GitHub")
                        .OnClick(() => client.OpenUrl(issueUrl))
                    : null);
            var actionsCell = new TableCell(actionsContent).Width(Size.Px(100)).AlignContent(Align.Right);

            return new TableRow(checkboxCell, titleCell, repoCell, labelsCell, assigneesCell, actionsCell);
        }).ToArray();

        var table = new Table(new[] { headerRow }.Concat(dataRows).ToArray()).Width(Size.Full());

        return new HeaderLayout(fullHeader, table);
    }
}
