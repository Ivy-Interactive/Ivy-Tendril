using System.Text.RegularExpressions;
using Ivy.Core;
using Ivy.Tendril.Apps;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Views.Tabs;

public static class GitTabHelper
{
    public record GitTabData(
        List<WorktreeSection> WorktreeSections,
        List<PlanContentHelpers.CommitRow> UnassociatedCommitRows
    );

    public record WorktreeSection(
        string Name,
        string Path,
        string Branch,
        string ShortHash,
        bool HasUncommittedChanges,
        List<PlanContentHelpers.CommitRow> CommitRows,
        string? ParentRepoPath = null,
        string? ParentBranch = null,
        string? ParentShortHash = null
    );

    public static GitTabData BuildGitTabData(
        PlanFile plan,
        IConfigService config,
        IGitService gitService)
    {
        var commitRows = PlanContentHelpers.BuildCommitRows(plan, config, gitService);
        return BuildGitTabDataInternal(plan, config, gitService, commitRows);
    }

    public static GitTabData BuildGitTabData(
        List<PlanContentHelpers.CommitRow> precomputedCommitRows,
        PlanFile plan,
        IConfigService config,
        IGitService gitService)
    {
        return BuildGitTabDataInternal(plan, config, gitService, precomputedCommitRows);
    }

    private static GitTabData BuildGitTabDataInternal(
        PlanFile plan,
        IConfigService config,
        IGitService gitService,
        List<PlanContentHelpers.CommitRow> allCommitRows)
    {
        var sections = new List<WorktreeSection>();
        var assignedCommitHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var worktreesDir = Path.Combine(plan.FolderPath, "worktrees");

        if (Directory.Exists(worktreesDir))
        {
            foreach (var repoDir in Directory.GetDirectories(worktreesDir))
            {
                var section = BuildSectionForWorktree(repoDir, allCommitRows, assignedCommitHashes, gitService);
                if (section != null)
                    sections.Add(section);
            }
        }

        var unassigned = allCommitRows
            .Where(r => !assignedCommitHashes.Contains(r.Hash))
            .ToList();

        return new GitTabData(sections, unassigned);
    }

    private static WorktreeSection? BuildSectionForWorktree(
        string repoDir,
        List<PlanContentHelpers.CommitRow> allCommitRows,
        HashSet<string> assignedCommitHashes,
        IGitService gitService)
    {
        var worktreesResult = gitService.GetWorktrees(repoDir);
        if (!worktreesResult.IsSuccess || worktreesResult.Value == null) return null;

        var worktree = worktreesResult.Value.FirstOrDefault(w =>
            Path.GetFullPath(w.Path).Equals(Path.GetFullPath(repoDir), StringComparison.OrdinalIgnoreCase));

        if (worktree == null) return null;

        var shortHash = worktree.CommitHash.Length > 7
            ? worktree.CommitHash[..7]
            : worktree.CommitHash;

        var hasUncommitted = false;
        var statusResult = gitService.HasUncommittedChanges(repoDir);
        if (statusResult.IsSuccess)
            hasUncommitted = statusResult.Value;

        var worktreeCommits = new List<PlanContentHelpers.CommitRow>();
        if (allCommitRows.Count > 0)
        {
            var candidateHashes = allCommitRows
                .Where(r => !assignedCommitHashes.Contains(r.Hash))
                .Select(r => r.Hash)
                .ToList();

            var reachableResult = gitService.GetReachableCommits(repoDir, candidateHashes);
            if (reachableResult.IsSuccess && reachableResult.Value != null)
            {
                var reachableSet = new HashSet<string>(reachableResult.Value, StringComparer.OrdinalIgnoreCase);
                worktreeCommits = allCommitRows
                    .Where(r => reachableSet.Contains(r.Hash))
                    .ToList();
                foreach (var c in worktreeCommits)
                    assignedCommitHashes.Add(c.Hash);
            }
        }

        var (parentRepoPath, parentBranch, parentShortHash) = ResolveParentInfo(repoDir, worktreesResult.Value, gitService);

        return new WorktreeSection(
            Path.GetFileName(repoDir),
            repoDir,
            worktree.Branch,
            shortHash,
            hasUncommitted,
            worktreeCommits,
            parentRepoPath,
            parentBranch,
            parentShortHash
        );
    }

    public static object RenderGitTab(
        GitTabData gitData,
        PlanFile plan,
        IClientProvider client,
        IConfigService config,
        Action<string?> setOpenCommit,
        Func<string, object> copyToClipboard,
        HashSet<string>? syncingPaths = null,
        Action<string>? onSynchronize = null,
        ILogger? logger = null)
    {
        var gitLayout = Layout.Vertical().Gap(4);

        foreach (var section in gitData.WorktreeSections)
        {
            var isSyncing = syncingPaths?.Contains(section.Path) == true;
            gitLayout |= RenderWorktreeSection(section, isSyncing, client, config, setOpenCommit, copyToClipboard, onSynchronize, logger);
        }

        // Unassociated commits (commits not in any worktree)
        if (gitData.UnassociatedCommitRows.Count > 0)
        {
            var commitWarning = PlanContentHelpers.BuildCommitWarningCallout(gitData.UnassociatedCommitRows);
            if (commitWarning != null)
                gitLayout |= commitWarning;

            gitLayout |= Text.Block("Commits").Bold();
            gitLayout |= RenderCommitTable(gitData.UnassociatedCommitRows, setOpenCommit);
        }

        // Pull requests section
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

        // Empty state
        if (gitData.WorktreeSections.Count == 0 && gitData.UnassociatedCommitRows.Count == 0 && plan.Prs.Count == 0)
        {
            gitLayout |= Text.Muted("No worktrees, commits, or pull requests yet.");
        }

        return gitLayout;
    }

    private static object RenderWorktreeSection(
        WorktreeSection section,
        bool isSyncing,
        IClientProvider client,
        IConfigService config,
        Action<string?> setOpenCommit,
        Func<string, object> copyToClipboard,
        Action<string>? onSynchronize,
        ILogger? logger)
    {
        var sectionLayout = Layout.Vertical().Gap(2);

        // Header row: name + ... menu
        var syncMenuItem = new MenuItem("Synchronize", Icon: Icons.RefreshCw, Tag: "Synchronize");
        if (onSynchronize != null && !isSyncing)
            syncMenuItem = syncMenuItem.OnSelect(() => onSynchronize(section.Path));

        var headerRow = Layout.Horizontal().Gap(2).AlignContent(Align.SpaceBetween)
            | (Layout.Horizontal().Gap(2).AlignContent(Align.Left)
                | Text.Block(section.Name).Bold())
            | new Button().Icon(Icons.EllipsisVertical).Ghost().Small()
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

        // "Based on" line
        sectionLayout |= Layout.Horizontal().Gap(1).AlignContent(Align.Left)
            | Text.Muted("Based on:")
            | new Badge($"{section.Branch}@{section.ShortHash}").Variant(BadgeVariant.Outline).Small();

        // Worktrees tile
        if (section.ParentRepoPath != null)
        {
            var worktreeDetails = Layout.Vertical().Gap(1);
            worktreeDetails |= Layout.Horizontal().Gap(1)
                | Text.Muted("Repository:")
                | Text.Block(section.ParentRepoPath.Replace('\\', '/'));
            if (section.ParentBranch != null && section.ParentShortHash != null)
            {
                worktreeDetails |= Layout.Horizontal().Gap(1)
                    | Text.Muted("Parent:")
                    | Text.Block($"{section.ParentBranch}@{section.ParentShortHash}");
            }
            worktreeDetails |= Layout.Horizontal().Gap(1)
                | Text.Muted("Worktree:")
                | Text.Block(section.Path.Replace('\\', '/'));
            worktreeDetails |= Layout.Horizontal().Gap(1)
                | Text.Muted("Head:")
                | Text.Block($"{section.Branch}@{section.ShortHash}");

            sectionLayout |= new Card(worktreeDetails).Title("Worktrees");
        }

        // Syncing state
        if (isSyncing)
        {
            sectionLayout |= Text.Muted("Synchronizing...");
            return sectionLayout;
        }

        // Warning callout for uncommitted changes
        if (section.HasUncommittedChanges)
        {
            sectionLayout |= Callout.Warning("This worktree has uncommitted changes");
        }

        // Commit warning callout
        var commitWarning = PlanContentHelpers.BuildCommitWarningCallout(section.CommitRows);
        if (commitWarning != null)
            sectionLayout |= commitWarning;

        // Commits table or empty state
        if (section.CommitRows.Count > 0)
        {
            sectionLayout |= RenderCommitTable(section.CommitRows, setOpenCommit);
        }
        else
        {
            sectionLayout |= Text.Muted("(no commits)");
        }

        return sectionLayout;
    }

    private static object RenderCommitTable(
        List<PlanContentHelpers.CommitRow> commitRows,
        Action<string?> setOpenCommit)
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
                new Button(shortHash).Inline().OnClick(() =>
                {
                    var row = tableRows.First(r => r.Commit == shortHash);
                    setOpenCommit(row.Hash);
                })))
            .Remove(t => t.Hash);
    }

    private static (string? Path, string? Branch, string? ShortHash) ResolveParentInfo(
        string repoDir, List<WorktreeInfo> worktrees, IGitService gitService)
    {
        var mainWorktree = worktrees.FirstOrDefault(w =>
            !Path.GetFullPath(w.Path).Equals(Path.GetFullPath(repoDir), StringComparison.OrdinalIgnoreCase));

        if (mainWorktree != null)
        {
            return (
                mainWorktree.Path,
                mainWorktree.Branch,
                mainWorktree.CommitHash.Length > 9 ? mainWorktree.CommitHash[..9] : mainWorktree.CommitHash
            );
        }

        var resolvedRoot = ResolveRepoRootFromWorktree(repoDir);
        if (resolvedRoot == null) return (null, null, null);

        var parentWorktrees = gitService.GetWorktrees(resolvedRoot);
        if (!parentWorktrees.IsSuccess || parentWorktrees.Value == null) return (resolvedRoot, null, null);

        var main = parentWorktrees.Value.FirstOrDefault(w =>
            Path.GetFullPath(w.Path).Equals(Path.GetFullPath(resolvedRoot), StringComparison.OrdinalIgnoreCase));

        if (main == null) return (resolvedRoot, null, null);

        return (
            resolvedRoot,
            main.Branch,
            main.CommitHash.Length > 9 ? main.CommitHash[..9] : main.CommitHash
        );
    }

    private static string? ResolveRepoRootFromWorktree(string wtDir)
    {
        var gitFile = Path.Combine(wtDir, ".git");
        if (!File.Exists(gitFile)) return null;
        var gitContent = FileHelper.ReadAllText(gitFile).Trim();
        var gitDirMatch = Regex.Match(gitContent, @"gitdir:\s*(.+)");
        if (!gitDirMatch.Success) return null;

        var gitDir = gitDirMatch.Groups[1].Value.Trim();
        var repoGitDir = Path.GetFullPath(Path.Combine(wtDir, gitDir, "..", ".."));
        var repoRoot = Path.GetDirectoryName(repoGitDir);
        return repoRoot != null && Directory.Exists(repoRoot) ? repoRoot : null;
    }

    private record CommitTableRow(string Commit, string Hash, string Message, string Files);
    private record PrTableRow(string Repository, string Pr);
}
