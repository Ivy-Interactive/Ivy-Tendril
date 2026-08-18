using Ivy.Tendril.Apps.PullRequest;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Apps.Views.Tabs;

public class GitTabView(
    GitTabDataBuilder.GitTabData gitData,
    PlanFile plan,
    Action<string?> setOpenCommit,
    Func<string, object> copyToClipboard,
    HashSet<string>? syncingPaths,
    Action<string>? onSynchronize) : ViewBase
{
    public override object Build()
    {
        var client = UseService<IClientProvider>();
        var config = UseService<IConfigService>();
        var logger = UseService<ILogger<GitTabView>>();

        var gitLayout = Layout.Vertical().Gap(8);

        gitLayout |= Text.H2("Worktrees");

        if (gitData.WorktreeSections.Count == 0)
        {
            gitLayout |= RenderNoWorktreesState();
        }

        foreach (var section in gitData.WorktreeSections)
        {
            var isSyncing = syncingPaths?.Contains(section.Path) == true;
            gitLayout |= RenderWorktreeSection(section, isSyncing, client, config, logger);
        }

        if (gitData.UnassociatedCommitRows.Count > 0)
        {
            var commitWarning = PlanContentHelpers.BuildCommitWarningCallout(gitData.UnassociatedCommitRows);
            if (commitWarning != null)
                gitLayout |= commitWarning;

            gitLayout |= Text.Block("Commits").Bold();

            var refStatus = gitData.UnassociatedCommitRefStatus;
            var unreachableCallout = BuildUnreachableCommitsCallout(gitData.UnassociatedCommitRows, refStatus);
            if (unreachableCallout != null)
                gitLayout |= unreachableCallout;

            gitLayout |= RenderCommitTable(gitData.UnassociatedCommitRows, refStatus);
        }

        if (plan.Prs.Count > 0)
        {
            if (gitData.WorktreeSections.Count > 0 || gitData.UnassociatedCommitRows.Count > 0)
                gitLayout |= new Separator();

            gitLayout |= Text.Block("Pull Requests").Bold();

            var prTableRows = plan.Prs.Where(PullRequestApp.IsValidUrl)
                .Select(pr => new PrTableRow(PullRequestApp.ExtractRepo(pr), pr))
                .ToList();

            gitLayout |= new TableBuilder<PrTableRow>(prTableRows)
                .Header(t => t.Pr, "PR")
                .Builder(t => t.Pr, f => f.Func<PrTableRow, string>(url =>
                    new Button(url).Link().OnClick(() => client.OpenUrl(url))));
        }

        if (gitData.WorktreeSections.Count == 0 && gitData.UnassociatedCommitRows.Count == 0 && plan.Prs.Count == 0)
        {
            gitLayout |= Text.Muted("No worktrees, commits, or pull requests yet.");
        }

        return gitLayout;
    }

    /// <summary>
    ///     Replaces the bare "Worktrees" heading over empty space with a statement of why there is
    ///     nothing to list. The tab is only rendered at all when the plan has commits or PRs, so an
    ///     empty worktree list means the worktree went away, not that nothing ever happened.
    /// </summary>
    private object RenderNoWorktreesState()
    {
        var reason = plan.Status switch
        {
            PlanStatus.Completed or PlanStatus.Skipped or PlanStatus.Icebox =>
                "removed after the plan reached its final state",
            PlanStatus.Failed or PlanStatus.Draft =>
                "never created, or reclaimed by the stale reaper once the plan sat idle past its window",
            _ => "removed, or never created"
        };

        return new Card() |
               (Layout.Vertical().Gap(0).AlignContent(Align.Center)
                | Icons.GitBranchPlus.ToIcon().Color(Colors.Muted)
                | Text.Muted($"No worktrees — {reason}"));
    }

    /// <summary>
    ///     Warns when commits the plan recorded are held by no ref. Such a commit exists only as a
    ///     loose object and is destroyed by the next <c>git gc</c> in its repo, so saying so is the
    ///     difference between recovering the work and losing it.
    /// </summary>
    private static object? BuildUnreachableCommitsCallout(
        List<PlanContentHelpers.CommitRow> commitRows,
        IReadOnlyDictionary<string, CommitRefStatus>? refStatus)
    {
        if (refStatus == null || refStatus.Count == 0) return null;

        var unreachable = commitRows.Where(r => StatusOf(r, refStatus) == CommitRefStatus.Unreachable).ToList();
        var missing = commitRows.Where(r => StatusOf(r, refStatus) == CommitRefStatus.Missing).ToList();
        if (unreachable.Count == 0 && missing.Count == 0) return null;

        var parts = new List<string>();

        if (unreachable.Count > 0)
        {
            var hashes = string.Join(", ", unreachable.Select(r => $"`{r.ShortHash}`"));
            parts.Add(
                $"{Count(unreachable.Count, "commit")} reachable from no branch, tag or remote: {hashes}. " +
                "They exist only as loose objects, so the next `git gc` in the repo destroys them. " +
                "Give one a ref to keep it: `git branch recover/<name> <hash>`.");
        }

        if (missing.Count > 0)
        {
            var hashes = string.Join(", ", missing.Select(r => $"`{r.ShortHash}`"));
            parts.Add(
                $"{Count(missing.Count, "commit")} not found in the plan's repos at all: {hashes}. " +
                "Either the object was already pruned, or the repo it was made in is not one of the plan's repos.");
        }

        return Callout.Error(string.Join("\n\n", parts), "Commits At Risk");

        static CommitRefStatus StatusOf(PlanContentHelpers.CommitRow row, IReadOnlyDictionary<string, CommitRefStatus> map) =>
            map.TryGetValue(row.Hash, out var status) ? status : CommitRefStatus.Reachable;

        static string Count(int n, string noun) => n == 1 ? $"1 {noun} is" : $"{n} {noun}s are";
    }

    private object RenderWorktreeSection(
        GitTabDataBuilder.WorktreeSection section,
        bool isSyncing,
        IClientProvider client,
        IConfigService config,
        ILogger? logger)
    {
        var sectionLayout = Layout.Vertical();

        var syncMenuItem = new MenuItem("Synchronize", Icon: Icons.RefreshCw, Tag: "Synchronize");
        if (onSynchronize != null && !isSyncing)
            syncMenuItem = syncMenuItem.OnSelect(() => onSynchronize(section.Path));

        var headerRow = Layout.Horizontal().Width(Size.Full()).AlignContent(Align.SpaceBetween)
            | (Layout.Horizontal().Gap(2).AlignContent(Align.Left)
                | Icons.GitBranchPlus.ToIcon().Color(Colors.Muted)
                | Text.H3(section.Name))
            | new Button().Icon(Icons.EllipsisVertical).Ghost()
                .WithDropDown(
                    syncMenuItem,
                    new MenuItem("Open in File Manager", Icon: Icons.FolderOpen, Tag: "OpenInExplorer")
                        .OnSelect(() => PlatformHelper.OpenInFileManager(section.Path, logger)),
                    new MenuItem("Open in Terminal", Icon: Icons.Terminal, Tag: "OpenInTerminal")
                        .OnSelect(() => PlatformHelper.OpenInTerminal(section.Path, logger)),
                    new MenuItem($"Open in {config.Editor.Label}", Icon: Icons.Code, Tag: "OpenInEditor")
                        .OnSelect(() =>
                        {
                            try
                            {
                                config.OpenInEditor(section.Path);
                            }
                            catch (EditorNotAvailableException ex)
                            {
                                client.Toast(
                                    $"'{ex.Command}' not found in PATH. Install the shell command from {ex.Label} or update the editor command in Settings → Advanced.",
                                    "Editor Not Available",
                                    variant: ToastVariant.Destructive);
                            }
                        }),
                    new MenuItem("Copy Path to Clipboard", Icon: Icons.ClipboardCopy, Tag: "CopyPath")
                        .OnSelect(() => copyToClipboard(section.Path))
                );

        sectionLayout |= headerRow;

        if (section.ParentRepoPath != null)
        {
            List<Detail> detailsList = [
                new("Repository", Text.Monospaced(section.ParentRepoPath.Replace('\\', '/')))
            ];

            if (section is { BaseBranch: not null, BaseShortHash: not null })
            {
                detailsList.Add(new("Base", Text.Monospaced($"{section.BaseBranch}@{section.BaseShortHash}")));
            }

            detailsList.Add(new("Worktree", Text.Monospaced(section.Path.Replace('\\', '/'))));
            detailsList.Add(new("Head", Text.Monospaced($"{section.Branch}@{section.ShortHash}")));

            sectionLayout |= new Details(detailsList);
        }

        if (isSyncing)
        {
            sectionLayout |= Text.Muted("Synchronizing...");
            return sectionLayout;
        }

        if (section.HasUncommittedChanges)
        {
            sectionLayout |= Callout.Warning("This worktree has uncommitted changes");
        }

        var commitWarning = PlanContentHelpers.BuildCommitWarningCallout(section.CommitRows);
        if (commitWarning != null)
            sectionLayout |= commitWarning;

        if (section.CommitRows.Count > 0)
        {
            sectionLayout |= RenderCommitTable(section.CommitRows);
        }
        else
        {
            sectionLayout |= new Card() |
                             (Layout.Vertical().Gap(0).AlignContent(Align.Center)
                              | Icons.GitCommitHorizontal.ToIcon().Color(Colors.Muted)
                              | Text.Muted("(no commits)"))
                ;
        }

        return sectionLayout;
    }

    private object RenderCommitTable(List<PlanContentHelpers.CommitRow> commitRows,
        IReadOnlyDictionary<string, CommitRefStatus>? refStatus = null)
    {
        var tableRows = commitRows.Select(row => new CommitTableRow(
            row.ShortHash,
            row.Hash,
            row.Title,
            row.FileCount?.ToString() ?? "–"
        )).ToList();

        return new TableBuilder<CommitTableRow>(tableRows)
            .Order(t => t.Commit, t => t.Message, t => t.Files)
            .Builder(t => t.Commit, f => f.Func<CommitTableRow, string>(shortHash =>
            {
                var row = tableRows.First(r => r.Commit == shortHash);
                var link = new Button(shortHash).Inline().OnClick(() => setOpenCommit(row.Hash));

                var status = refStatus != null && refStatus.TryGetValue(row.Hash, out var s)
                    ? s
                    : CommitRefStatus.Reachable;
                if (status == CommitRefStatus.Reachable) return link;

                return Layout.Horizontal().Gap(2).AlignContent(Align.Left)
                       | link
                       | new Badge(status == CommitRefStatus.Missing ? "not found" : "unreachable")
                           .Variant(BadgeVariant.Destructive).Small();
            }))
            .Remove(t => t.Hash)
            .ColumnWidth(t => t.Message, Size.Grow());
    }

    private record CommitTableRow(string Commit, string Hash, string Message, string Files);
    private record PrTableRow(string Repository, string Pr);
}
