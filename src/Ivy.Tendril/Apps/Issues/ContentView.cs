using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Apps.Views.Sheets;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Git;

namespace Ivy.Tendril.Apps.Issues;

public class ContentView(
    IState<GitHubIssue?> activeIssue,
    List<GitHubIssue> allFetchedIssues,
    IState<HashSet<int>> selectedIssueNumbers,
    RepoConfig? repoConfig,
    IState<bool> isImporting,
    IGithubService githubService,
    IConfigService config,
    Func<IReadOnlyList<GitHubIssue>, Task> onImportIssues) : ViewBase
{
    public override object Build()
    {
        var client = UseService<IClientProvider>();
        var openFile = UseState<string?>(null);

        if (activeIssue.Value is not { } issue)
        {
            if (allFetchedIssues.Count == 0)
                return new NoContentView("No Issues Loaded", "Select a GitHub repository and click Fetch Issues to browse.");

            return new NoContentView("No Issue Selected", "Select an issue from the sidebar to inspect its details.");
        }

        var currentIndex = allFetchedIssues.FindIndex(i => i.Number == issue.Number);
        var projectName = repoConfig != null
            ? IssuesApp.GetProjectForRepo(githubService, repoConfig.Owner, repoConfig.Name)
            : "Auto";
        var issueUrl = repoConfig != null
            ? $"https://github.com/{repoConfig.Owner}/{repoConfig.Name}/issues/{issue.Number}"
            : null;

        var titleArea = Layout.Vertical().Gap(1).AlignContent(Align.Left).Width(Size.Grow())
            | new Box(Text.Block($"#{issue.Number} {issue.Title}").Bold().NoWrap().Overflow(Overflow.Ellipsis))
                .BorderThickness(0).Padding(0).Width(Size.Full())
                .HideOn(Breakpoint.Mobile, Breakpoint.Tablet)
            | MobileItemPicker.Build(
                    $"#{issue.Number} {issue.Title}",
                    allFetchedIssues,
                    i => $"#{i.Number} {i.Title}",
                    i => i.Number == issue.Number,
                    i => activeIssue.Set(i))
                .ShowOn(Breakpoint.Mobile, Breakpoint.Tablet);

        var controls = Layout.Horizontal().Gap(2).AlignContent(Align.Right)
            | Text.Rich()
                .Bold($"{currentIndex + 1}/{allFetchedIssues.Count}", word: true)
                .Muted("issues", word: true);

        var topRow = Layout.Horizontal().Height(Size.Px(40)).Width(Size.Full()).Gap(2).AlignContent(Align.Left)
            | titleArea
            | controls;

        var metaRow = Layout.Horizontal().Gap(2).AlignContent(Align.Left).Wrap()
            | new Badge(projectName).Variant(BadgeVariant.Outline).Small().WithProjectColor(config, projectName)
            | (issueUrl != null
                ? new Button().Icon(Icons.ExternalLink).Ghost().Small()
                    .Tooltip("Open on GitHub")
                    .OnClick(() => client.OpenUrl(issueUrl))
                : null);

        if (issue.Assignees.Length > 0)
        {
            var assignees = issue.Assignees.Where(a => !string.IsNullOrWhiteSpace(a)).ToArray();
            if (assignees.Length > 0)
            {
                metaRow |= Layout.Horizontal().Gap(1).AlignContent(Align.Left).Wrap()
                    | Text.Muted("Assignees:").Small()
                    | assignees.Select(a => (object)new Badge(a).Variant(BadgeVariant.Secondary).Small()).ToArray();
            }
        }

        if (issue.Labels.Length > 0)
        {
            metaRow |= Layout.Horizontal().Gap(1).AlignContent(Align.Left).Wrap()
                | Text.Muted("Labels:").Small()
                | issue.Labels.Select(l => (object)new Badge(l).Variant(BadgeVariant.Outline).Small()).ToArray();
        }

        var header = Layout.Vertical().Gap(2).Width(Size.Full())
            | topRow
            | metaRow;

        var isSelected = selectedIssueNumbers.Value.Contains(issue.Number);
        var selectedCount = selectedIssueNumbers.Value.Count;
        var selectedIssues = allFetchedIssues.Where(i => selectedIssueNumbers.Value.Contains(i.Number)).ToList();

        var actionBar = Layout.Horizontal().AlignContent(Align.Left).Gap(1).Wrap()
            | new Button(isSelected ? "Selected for Import" : "Select for Import")
                .Icon(isSelected ? Icons.Check : Icons.Square)
                .Outline()
                .OnClick(() =>
                {
                    var next = new HashSet<int>(selectedIssueNumbers.Value);
                    if (!next.Remove(issue.Number))
                        next.Add(issue.Number);
                    selectedIssueNumbers.Set(next);
                })
            | new Button("Import to Inbox")
                .Icon(Icons.Download)
                .Outline()
                .Loading(isImporting.Value)
                .OnClick(async () => await onImportIssues([issue]))
            | new Button(selectedCount > 0 ? $"Import Selected ({selectedCount})" : "Import Selected")
                .Primary()
                .Loading(isImporting.Value)
                .Disabled(selectedCount == 0)
                .OnClick(async () => await onImportIssues(selectedIssues));

        var bodyContent = string.IsNullOrWhiteSpace(issue.Body)
            ? (object)Text.Muted("No description provided.")
            : new Markdown(MarkdownHelper.PrepareForDisplay(issue.Body, config))
                .Article()
                .DangerouslyAllowLocalFiles()
                .OnLinkClick(FileSheet.CreateLinkClickHandler(openFile));

        var scrollableContent = Layout.Vertical().Width(Size.Full().Max(Size.Units(200))).Padding(6, 2, 6, 2)
            | bodyContent;

        var mainContent = Layout.Vertical().Scroll(Scroll.Auto).Width(Size.Full())
            | scrollableContent;

        var mainLayout = new HeaderLayout(
            header,
            new FooterLayout(
                actionBar,
                mainContent
            ).Size(Size.Full())
        ).Scroll(Scroll.None).Size(Size.Full());

        return new Fragment(mainLayout, new FileSheet(openFile, config));
    }
}
