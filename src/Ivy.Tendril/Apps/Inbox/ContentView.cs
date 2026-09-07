using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Apps.Views.Sheets;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.IssueTrackers.Models;

namespace Ivy.Tendril.Apps.Inbox;

public record IssueRow
{
    public string Id { get; init; } = "";
    public bool Selected { get; init; }
    public string Key { get; init; } = "";
    public string Issue { get; init; } = "";
    public string Repository { get; init; } = "";
    public string[] Labels { get; init; } = [];
    public string Assignees { get; init; } = "";
}

public record ReviewRow
{
    public string Id { get; init; } = "";
    public string Key { get; init; } = "";
    public string Review { get; init; } = "";
    public string Provider { get; init; } = "";
    public string Repository { get; init; } = "";
    public string Branch { get; init; } = "";
    public string Updated { get; init; } = "";
}

public class ContentView(
    IState<InboxCategory> selectedCategory,
    IState<string?> selectedProject,
    IReadOnlyList<ProjectConfig> projects,
    IState<HashSet<string>> selectedIssueIds,
    IReadOnlyList<TrackerIssue> myIssues,
    IReadOnlyList<TrackerReviewItem> reviewRequests,
    IReadOnlyList<TrackerIssue> projectIssues,
    bool isFetching,
    string? errorMessage,
    IState<bool> isImporting,
    IConfigService config,
    RefreshToken refreshToken,
    Func<Task> onRefresh,
    Func<IReadOnlyList<TrackerIssue>, Task> onFireOffIssues) : ViewBase
{
    public override object Build()
    {
        var client = UseService<IClientProvider>();
        var openFile = UseState<string?>(null);

        var (issueSheet, showIssueSheet) = UseTrigger<TrackerIssue>((isOpen, issue) =>
        {
            if (!isOpen.Value || issue == null) return null;

            var providerDisplayName = issue.ProviderId switch
            {
                "jira" => "Jira",
                "linear" => "Linear",
                "github" => "GitHub",
                "gitlab" => "GitLab",
                _ => issue.ProviderId
            };

            var sheetHeader = Layout.Vertical().Width(Size.Full())
                | (Layout.Horizontal().Height(Size.Auto()).AlignContent(Align.SpaceBetween).Width(Size.Full())
                    | (Layout.Horizontal().Height(Size.Auto()).Width(Size.Auto()).AlignContent(Align.Left).Wrap()
                        | new Badge(providerDisplayName).Variant(BadgeVariant.Outline).Small()
                        | (!string.IsNullOrEmpty(issue.Scope) ? new Badge(issue.Scope).Variant(BadgeVariant.Secondary).Small() : null)
                        | (!string.IsNullOrEmpty(issue.Status) && issue.Status != "Open" ? new Badge(issue.Status).Variant(BadgeVariant.Secondary).Small() : null)
                        | (!string.IsNullOrEmpty(issue.Priority) ? new Badge(issue.Priority).Variant(BadgeVariant.Outline).Small() : null)
                        | (issue.Assignees.Length > 0 ? Text.Muted($"Assigned: {string.Join(", ", issue.Assignees.Where(a => !string.IsNullOrWhiteSpace(a)))}").Small() : null))
                    | (Layout.Horizontal().Height(Size.Auto()).Width(Size.Auto()).AlignContent(Align.Right)
                        | (issue.Url != null
                            ? new Button(providerDisplayName)
                                .Icon(Icons.ExternalLink)
                                .Ghost().Small()
                                .OnClick(() => client.OpenUrl(issue.Url))
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
                    ? (Layout.Horizontal().Height(Size.Auto()).AlignContent(Align.Left).Wrap()
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
                | sheetBody;

            var sheet = new Sheet(
                () => isOpen.Set(false),
                sheetContent,
                $"{issue.Key} {issue.Title}"
            ).Width(UxHelper.SheetWidth).Resizable();

            return sheet;
        });

        var (reviewSheet, showReviewSheet) = UseTrigger<TrackerReviewItem>((isOpen, review) =>
        {
            if (!isOpen.Value || review == null) return null;

            var providerDisplayName = review.ProviderId switch
            {
                "gitlab" => "GitLab",
                "github" => "GitHub",
                _ => review.ProviderId
            };

            var sheetHeader = Layout.Vertical().Width(Size.Full())
                | (Layout.Horizontal().Height(Size.Auto()).AlignContent(Align.SpaceBetween).Width(Size.Full())
                    | (Layout.Horizontal().Height(Size.Auto()).Width(Size.Auto()).AlignContent(Align.Left).Wrap()
                        | new Badge(providerDisplayName).Variant(BadgeVariant.Outline).Small()
                        | new Badge(review.Repository).Variant(BadgeVariant.Secondary).Small()
                        | (!string.IsNullOrEmpty(review.Branch) ? new Badge(review.Branch).Variant(BadgeVariant.Outline).Small() : null)
                        | (review.Assignees.Length > 0 ? Text.Muted($"Assigned: {string.Join(", ", review.Assignees.Where(a => !string.IsNullOrWhiteSpace(a)))}").Small() : null))
                    | (Layout.Horizontal().Height(Size.Auto()).Width(Size.Auto()).AlignContent(Align.Right)
                        | new Button($"Open on {providerDisplayName}")
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
                | sheetBody;

            var sheet = new Sheet(
                () => isOpen.Set(false),
                sheetContent,
                $"{review.Key} {review.Title}"
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
                allIssues: projectIssues,
                showRepoBadge: false,
                client: client,
                showIssueSheet: showIssueSheet
            );
        }

        return new Fragment(mainView, issueSheet, reviewSheet, new FileSheet(openFile, config));
    }

    private object BuildReviewsView(IClientProvider client, Action<TrackerReviewItem> showReviewSheet)
    {
        var refreshButton = new Button()
            .Icon(Icons.RefreshCw)
            .Ghost()
            .Tooltip("Refresh")
            .Loading(isFetching)
            .OnClick(async () => await onRefresh());

        var header = Layout.Horizontal().Height(Size.Auto()).AlignContent(Align.SpaceBetween).Width(Size.Full())
            | (Layout.Horizontal().Height(Size.Auto()).Width(Size.Auto()).AlignContent(Align.Left)
                | Text.H3("Reviews").Bold()
                | refreshButton);

        if (isFetching && reviewRequests.Count == 0)
        {
            return Layout.Vertical().Height(Size.Full())
                | header
                | (Layout.Vertical().AlignContent(Align.Center).Height(Size.Grow())
                    | new Loading()
                    | Text.Muted("Fetching review requests..."));
        }

        if (errorMessage != null)
        {
            return Layout.Vertical().Height(Size.Full())
                | header
                | (Layout.Vertical().AlignContent(Align.Center).Height(Size.Grow())
                    | Text.Danger(errorMessage)
                    | new Button("Retry").Outline().OnClick(async () => await onRefresh()));
        }

        if (reviewRequests.Count == 0)
        {
            return Layout.Vertical().Height(Size.Full())
                | header
                | new NoContentView("All Caught Up!", "No pull requests currently require your review.");
        }

        var rows = reviewRequests.Select(pr => new ReviewRow
        {
            Id = pr.Id,
            Key = pr.Key,
            Review = $"{pr.Key} {pr.Title}",
            Provider = pr.ProviderId switch
            {
                "gitlab" => "GitLab",
                "github" => "GitHub",
                _ => pr.ProviderId
            },
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
                e => e.Provider,
                e => e.Repository,
                e => e.Branch,
                e => e.Updated
            )
            .Header(t => t.Review, "Pull Request")
            .Header(t => t.Provider, "Provider")
            .Header(t => t.Repository, "Repository")
            .Header(t => t.Branch, "Branch")
            .Header(t => t.Updated, "Updated")
            .Width(t => t.Review, Size.Fraction(0.45f))
            .Width(t => t.Provider, Size.Px(100))
            .Width(t => t.Repository, Size.Px(180))
            .Width(t => t.Branch, Size.Px(160))
            .Width(t => t.Updated, Size.Px(100))
            .Renderer(t => t.Repository, new LabelsDisplayRenderer())
            .Hidden(t => t.Id)
            .Hidden(t => t.Key)
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
                    var raw = reviewRequests.FirstOrDefault(r => r.Id == row.Id);
                    if (raw != null) showReviewSheet(raw);
                }
                return ValueTask.CompletedTask;
            })
            .RowActions(
                new MenuItem("Open in Browser", Icon: Icons.ExternalLink, Tag: "open-browser").Tooltip("Open pull request in browser"),
                new MenuItem("View Details", Icon: Icons.FileText, Tag: "view-details").Tooltip("View review details")
            )
            .OnRowAction(e =>
            {
                var tag = e.Value.Tag?.ToString();
                var id = e.Value.Id?.ToString();
                var row = rows.FirstOrDefault(r => r.Id == id);
                if (row != null)
                {
                    var raw = reviewRequests.FirstOrDefault(r => r.Id == row.Id);
                    if (raw != null)
                    {
                        if (tag == "open-browser") client.OpenUrl(raw.Url);
                        else if (tag == "view-details") showReviewSheet(raw);
                    }
                }
                return ValueTask.CompletedTask;
            });

        if (rows.All(r => string.IsNullOrEmpty(r.Branch)))
        {
            dataTable = dataTable.Hidden(t => t.Branch);
        }

        return Layout.Vertical().Height(Size.Full())
            | header
            | dataTable;
    }

    private object BuildIssuesView(
        string title,
        IReadOnlyList<TrackerIssue> allIssues,
        bool showRepoBadge,
        IClientProvider client,
        Action<TrackerIssue> showIssueSheet)
    {
        var refreshButton = new Button()
            .Icon(Icons.RefreshCw)
            .Ghost()
            .Tooltip("Refresh")
            .Loading(isFetching)
            .OnClick(async () => await onRefresh());

        var selectedCount = selectedIssueIds.Value.Count;
        var selectedIssuesList = allIssues
            .Where(i => selectedIssueIds.Value.Contains(i.Id))
            .ToList();

        void SelectAll()
        {
            var next = new HashSet<string>(selectedIssueIds.Value);
            foreach (var i in allIssues) next.Add(i.Id);
            selectedIssueIds.Set(next);
            refreshToken.Refresh();
        }

        void DeselectAll()
        {
            var next = new HashSet<string>(selectedIssueIds.Value);
            foreach (var i in allIssues) next.Remove(i.Id);
            selectedIssueIds.Set(next);
            refreshToken.Refresh();
        }

        var header = Layout.Horizontal().Height(Size.Auto()).AlignContent(Align.SpaceBetween).Width(Size.Full())
            | (Layout.Horizontal().Height(Size.Auto()).Width(Size.Auto()).AlignContent(Align.Left)
                | Text.H3(title).Bold()
                | refreshButton)
            | (Layout.Horizontal().Height(Size.Auto()).Width(Size.Auto()).AlignContent(Align.Right)
                | new Button("Select All").Ghost().Small().OnClick(SelectAll)
                | new Button("Deselect All").Ghost().Small().Disabled(selectedCount == 0).OnClick(DeselectAll)
                | Text.Muted($"{selectedCount} of {allIssues.Count} selected").Small()
                | new Button(selectedCount > 0 ? $"Fire off in Tendril ({selectedCount})" : "Fire off in Tendril")
                    .Icon(Icons.Zap)
                    .Primary().Small()
                    .Disabled(selectedCount == 0 || isImporting.Value)
                    .Loading(isImporting.Value)
                    .OnClick(async () => await onFireOffIssues(selectedIssuesList)));

        if (isFetching && allIssues.Count == 0)
        {
            return Layout.Vertical().Height(Size.Full())
                | header
                | (Layout.Vertical().AlignContent(Align.Center).Height(Size.Grow())
                    | new Loading()
                    | Text.Muted("Loading issues..."));
        }

        if (errorMessage != null)
        {
            return Layout.Vertical().Height(Size.Full())
                | header
                | (Layout.Vertical().AlignContent(Align.Center).Height(Size.Grow())
                    | Text.Danger(errorMessage)
                    | new Button("Retry").Outline().OnClick(async () => await onRefresh()));
        }

        if (allIssues.Count == 0)
        {
            return Layout.Vertical().Height(Size.Full())
                | header
                | new NoContentView("No Issues Found", "No issues match the selected view.");
        }

        var rows = allIssues.Select(issue => new IssueRow
        {
            Id = issue.Id,
            Selected = selectedIssueIds.Value.Contains(issue.Id),
            Key = issue.Key,
            Issue = $"{issue.Key} {issue.Title}",
            Repository = issue.Scope ?? "",
            Labels = issue.Labels,
            Assignees = string.Join(", ", issue.Assignees.Where(a => !string.IsNullOrWhiteSpace(a)))
        }).ToList();

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
            .Hidden(t => t.Key)
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
                    var next = new HashSet<string>(selectedIssueIds.Value);
                    if (!next.Remove(row.Id)) next.Add(row.Id);
                    selectedIssueIds.Set(next);
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
                    var raw = allIssues.FirstOrDefault(i => i.Id == row.Id);
                    if (raw != null) showIssueSheet(raw);
                }
                return ValueTask.CompletedTask;
            })
            .RowActions(
                new MenuItem("Fire off in Tendril", Icon: Icons.Zap, Tag: "fire-off").Tooltip("Fire off this issue in Tendril"),
                new MenuItem("View Details", Icon: Icons.FileText, Tag: "view-details").Tooltip("View issue details"),
                new MenuItem("Open in Browser", Icon: Icons.ExternalLink, Tag: "open-browser").Tooltip("Open issue in browser")
            )
            .OnRowAction(async e =>
            {
                var tag = e.Value.Tag?.ToString();
                var id = e.Value.Id?.ToString();
                var row = rows.FirstOrDefault(r => r.Id == id);
                if (row != null)
                {
                    var raw = allIssues.FirstOrDefault(i => i.Id == row.Id);
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
                        else if (tag == "open-browser")
                        {
                            if (!string.IsNullOrEmpty(raw.Url)) client.OpenUrl(raw.Url);
                        }
                    }
                }
            });

        return Layout.Vertical().Height(Size.Full())
            | header
            | dataTable;
    }
}
