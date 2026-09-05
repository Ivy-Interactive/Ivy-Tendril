using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Apps.Views.Sheets;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Git;

namespace Ivy.Tendril.Apps.Inbox;

public record IssueRow
{
    public string Id { get; init; } = "";
    public bool Selected { get; init; }
    public int Number { get; init; }
    public string Issue { get; init; } = "";
    public string Repository { get; init; } = "";
    public string[] Labels { get; init; } = [];
    public string Assignees { get; init; } = "";
}

public record ReviewRow
{
    public string Id { get; init; } = "";
    public int Number { get; init; }
    public string Review { get; init; } = "";
    public string Repository { get; init; } = "";
    public string Branch { get; init; } = "";
    public string Updated { get; init; } = "";
}

public class ContentView(
    IState<InboxCategory> selectedCategory,
    IState<string?> selectedProject,
    IReadOnlyList<ProjectConfig> projects,
    IState<HashSet<int>> selectedIssueNumbers,
    IReadOnlyList<GitHubIssue> myIssues,
    IReadOnlyList<GitHubReviewItem> reviewRequests,
    IReadOnlyList<GitHubIssue> projectIssues,
    bool isFetching,
    string? errorMessage,
    IState<bool> isImporting,
    IConfigService config,
    IGithubService githubService,
    RefreshToken refreshToken,
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
                client: client,
                showIssueSheet: showIssueSheet
            );
        }

        return new Fragment(mainView, issueSheet, reviewSheet, new FileSheet(openFile, config));
    }

    private object BuildReviewsView(IClientProvider client, Action<GitHubReviewItem> showReviewSheet)
    {
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

        if (reviewRequests.Count == 0)
        {
            return new HeaderLayout(
                headerToolbar,
                new NoContentView("All Caught Up!", "No pull requests currently require your review.")
            );
        }

        var rows = reviewRequests.Select(pr => new ReviewRow
        {
            Id = $"{pr.Repository}#{pr.Number}",
            Number = pr.Number,
            Review = $"#{pr.Number} {pr.Title}",
            Repository = pr.Repository,
            Branch = pr.Branch ?? "",
            Updated = pr.UpdatedAt.HasValue ? pr.UpdatedAt.Value.ToString("M/d") : ""
        }).ToList();

        var dataTable = rows.AsQueryable()
            .ToDataTable(t => t.Id)
            .RefreshToken(refreshToken)
            .Width(Size.Full())
            .Height(Size.Full())
            .Order(
                e => e.Review,
                e => e.Repository,
                e => e.Branch,
                e => e.Updated
            )
            .Header(t => t.Review, "Pull Request")
            .Header(t => t.Repository, "Repository")
            .Header(t => t.Branch, "Branch")
            .Header(t => t.Updated, "Updated")
            .Width(t => t.Review, Size.Fraction(0.5f))
            .Width(t => t.Repository, Size.Px(180))
            .Width(t => t.Branch, Size.Px(160))
            .Width(t => t.Updated, Size.Px(100))
            .Renderer(t => t.Repository, new LabelsDisplayRenderer())
            .Hidden(t => t.Id)
            .Hidden(t => t.Number)
            .Config(c =>
            {
                c.AllowSorting = true;
                c.AllowFiltering = true;
                c.ShowSearch = true;
                c.SelectionMode = SelectionModes.None;
                c.ShowIndexColumn = false;
                c.BatchSize = 50;
            })
            .OnCellAction(t => t.Review, e =>
            {
                var id = e.Value.RowId?.ToString();
                var row = rows.FirstOrDefault(r => r.Id == id) ?? rows.ElementAtOrDefault(e.Value.RowIndex);
                if (row != null)
                {
                    var raw = reviewRequests.FirstOrDefault(r => r.Number == row.Number && r.Repository == row.Repository);
                    if (raw != null) showReviewSheet(raw);
                }
                return ValueTask.CompletedTask;
            })
            .RowActions(
                new MenuItem("Review on GitHub", Icon: Icons.ExternalLink, Tag: "open-github").Tooltip("Open pull request on GitHub"),
                new MenuItem("View Details", Icon: Icons.FileText, Tag: "view-details").Tooltip("View review details")
            )
            .OnRowAction(e =>
            {
                var tag = e.Value.Tag?.ToString();
                var id = e.Value.Id?.ToString();
                var row = rows.FirstOrDefault(r => r.Id == id);
                if (row != null)
                {
                    var raw = reviewRequests.FirstOrDefault(r => r.Number == row.Number && r.Repository == row.Repository);
                    if (raw != null)
                    {
                        if (tag == "open-github") client.OpenUrl(raw.Url);
                        else if (tag == "view-details") showReviewSheet(raw);
                    }
                }
                return ValueTask.CompletedTask;
            });

        return new HeaderLayout(headerToolbar, dataTable).Scroll(Scroll.None);
    }

    private object BuildIssuesView(
        string title,
        string subtitle,
        IReadOnlyList<GitHubIssue> allIssues,
        bool showRepoBadge,
        IClientProvider client,
        Action<GitHubIssue> showIssueSheet)
    {
        var refreshButton = new Button()
            .Icon(Icons.RefreshCw)
            .Ghost()
            .Tooltip("Refresh")
            .Loading(isFetching)
            .OnClick(async () => await onRefresh());

        var headerTop = Layout.Horizontal().AlignContent(Align.SpaceBetween).Width(Size.Full())
            | (Layout.Vertical().AlignContent(Align.Left)
                | Text.H3(title).Bold()
                | Text.Muted(subtitle))
            | (Layout.Horizontal().AlignContent(Align.Right)
                | refreshButton);

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

        if (allIssues.Count == 0)
        {
            return new HeaderLayout(
                headerTop,
                new NoContentView("No Issues Found", "No issues match the selected view.")
            );
        }

        var rows = allIssues.Select(issue => new IssueRow
        {
            Id = issue.Number.ToString(),
            Selected = selectedIssueNumbers.Value.Contains(issue.Number),
            Number = issue.Number,
            Issue = $"#{issue.Number} {issue.Title}",
            Repository = issue.Repository ?? "",
            Labels = issue.Labels,
            Assignees = string.Join(", ", issue.Assignees.Where(a => !string.IsNullOrWhiteSpace(a)))
        }).ToList();

        var selectedCount = selectedIssueNumbers.Value.Count;
        var selectedIssuesList = allIssues
            .Where(i => selectedIssueNumbers.Value.Contains(i.Number))
            .ToList();

        void SelectAll()
        {
            var next = new HashSet<int>(selectedIssueNumbers.Value);
            foreach (var i in allIssues) next.Add(i.Number);
            selectedIssueNumbers.Set(next);
            refreshToken.Refresh();
        }

        void DeselectAll()
        {
            var next = new HashSet<int>(selectedIssueNumbers.Value);
            foreach (var i in allIssues) next.Remove(i.Number);
            selectedIssueNumbers.Set(next);
            refreshToken.Refresh();
        }

        var batchBar = Layout.Horizontal().AlignContent(Align.SpaceBetween).Width(Size.Full())
            | (Layout.Horizontal().AlignContent(Align.Left)
                | new Button("Select All").Ghost().Small().OnClick(SelectAll)
                | new Button("Deselect All").Ghost().Small().Disabled(selectedCount == 0).OnClick(DeselectAll)
                | Text.Muted($"{selectedCount} of {rows.Count} selected").Small())
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

        var dataTable = rows.AsQueryable()
            .ToDataTable(t => t.Id)
            .RefreshToken(refreshToken)
            .Width(Size.Full())
            .Height(Size.Full())
            .Order(
                e => e.Selected,
                e => e.Issue,
                e => e.Repository,
                e => e.Labels,
                e => e.Assignees
            )
            .Header(t => t.Selected, "")
            .Header(t => t.Issue, "Issue")
            .Header(t => t.Repository, "Repository")
            .Header(t => t.Labels, "Labels")
            .Header(t => t.Assignees, "Assignees")
            .Width(t => t.Selected, Size.Px(45))
            .Width(t => t.Issue, Size.Fraction(0.45f))
            .Width(t => t.Repository, Size.Px(180))
            .Width(t => t.Labels, Size.Px(200))
            .Width(t => t.Assignees, Size.Px(150))
            .Renderer(t => t.Repository, new LabelsDisplayRenderer())
            .Hidden(t => t.Id)
            .Hidden(t => t.Number)
            .Config(c =>
            {
                c.AllowSorting = true;
                c.AllowFiltering = true;
                c.ShowSearch = true;
                c.SelectionMode = SelectionModes.None;
                c.ShowIndexColumn = false;
                c.BatchSize = 50;
            })
            .OnCellAction(t => t.Selected, e =>
            {
                var id = e.Value.RowId?.ToString();
                var row = rows.FirstOrDefault(r => r.Id == id) ?? rows.ElementAtOrDefault(e.Value.RowIndex);
                if (row != null)
                {
                    var next = new HashSet<int>(selectedIssueNumbers.Value);
                    if (!next.Remove(row.Number)) next.Add(row.Number);
                    selectedIssueNumbers.Set(next);
                    refreshToken.Refresh();
                }
                return ValueTask.CompletedTask;
            })
            .OnCellAction(t => t.Issue, e =>
            {
                var id = e.Value.RowId?.ToString();
                var row = rows.FirstOrDefault(r => r.Id == id) ?? rows.ElementAtOrDefault(e.Value.RowIndex);
                if (row != null)
                {
                    var raw = allIssues.FirstOrDefault(i => i.Number == row.Number);
                    if (raw != null) showIssueSheet(raw);
                }
                return ValueTask.CompletedTask;
            })
            .RowActions(
                new MenuItem("Fire off in Tendril", Icon: Icons.Zap, Tag: "fire-off").Tooltip("Fire off this issue in Tendril"),
                new MenuItem("View Details", Icon: Icons.FileText, Tag: "view-details").Tooltip("View issue details"),
                new MenuItem("Open in GitHub", Icon: Icons.ExternalLink, Tag: "open-github").Tooltip("Open issue on GitHub")
            )
            .OnRowAction(async e =>
            {
                var tag = e.Value.Tag?.ToString();
                var id = e.Value.Id?.ToString();
                var row = rows.FirstOrDefault(r => r.Id == id);
                if (row != null)
                {
                    var raw = allIssues.FirstOrDefault(i => i.Number == row.Number);
                    if (raw != null)
                    {
                        if (tag == "fire-off")
                        {
                            await onFireOffIssues([raw]);
                        }
                        else if (tag == "view-details")
                        {
                            showIssueSheet(raw);
                        }
                        else if (tag == "open-github")
                        {
                            var url = raw.Url ?? (raw.Repository != null
                                ? $"https://github.com/{raw.Repository}/issues/{raw.Number}"
                                : null);
                            if (url != null) client.OpenUrl(url);
                        }
                    }
                }
            });

        return new HeaderLayout(fullHeader, dataTable).Scroll(Scroll.None);
    }
}
