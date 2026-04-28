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
        List<PlanContentHelpers.CommitRow> CommitRows,
        List<WorktreeRow> WorktreeRows
    );

    public record WorktreeRow(
        string Name,
        string Path,
        string Branch,
        string ShortHash
    );

    public static GitTabData BuildGitTabData(
        PlanFile plan,
        IConfigService config,
        IGitService gitService)
    {
        var commitRows = PlanContentHelpers.BuildCommitRows(plan, config, gitService);
        var worktreeRows = BuildWorktreeRows(plan, config, gitService);

        return new GitTabData(commitRows, worktreeRows);
    }

    public static GitTabData BuildGitTabData(
        List<PlanContentHelpers.CommitRow> precomputedCommitRows,
        PlanFile plan,
        IConfigService config,
        IGitService gitService)
    {
        var worktreeRows = BuildWorktreeRows(plan, config, gitService);
        return new GitTabData(precomputedCommitRows, worktreeRows);
    }

    private static List<WorktreeRow> BuildWorktreeRows(
        PlanFile plan,
        IConfigService config,
        IGitService gitService)
    {
        var rows = new List<WorktreeRow>();
        var worktreesDir = Path.Combine(plan.FolderPath, "worktrees");

        if (!Directory.Exists(worktreesDir)) return rows;

        var repoDirs = Directory.GetDirectories(worktreesDir);
        foreach (var repoDir in repoDirs)
        {
            var worktreesResult = gitService.GetWorktrees(repoDir);
            if (!worktreesResult.IsSuccess || worktreesResult.Value == null) continue;

            // Find the worktree that matches this directory (not the main repo)
            var worktree = worktreesResult.Value.FirstOrDefault(w =>
                Path.GetFullPath(w.Path).Equals(Path.GetFullPath(repoDir), StringComparison.OrdinalIgnoreCase));

            if (worktree == null) continue;

            var shortHash = worktree.CommitHash.Length > 7
                ? worktree.CommitHash.Substring(0, 7)
                : worktree.CommitHash;

            rows.Add(new WorktreeRow(
                Path.GetFileName(repoDir),
                repoDir,
                worktree.Branch,
                shortHash
            ));
        }

        return rows;
    }

    public static object RenderGitTab(
        GitTabData gitData,
        PlanFile plan,
        IClientProvider client,
        IConfigService config,
        Action<string?> setOpenCommit,
        Func<string, object> copyToClipboard,
        ILogger? logger = null)
    {
        var gitLayout = Layout.Vertical().Gap(4);

        // Worktrees section
        if (gitData.WorktreeRows.Count > 0)
        {
            gitLayout |= Text.Block("Worktrees").Bold();

            var worktreeTableRows = gitData.WorktreeRows.Select(row => new WorktreeTableRow(
                row.Name,
                $"{row.Branch}:{row.ShortHash}",
                row.Path
            )).ToList();

            gitLayout |= new TableBuilder<WorktreeTableRow>(worktreeTableRows)
                .Header(t => t.BranchCommit, "Branch:Commit")
                .Builder(t => t.Actions, f => f.Func<WorktreeTableRow, string>(path =>
                    new Button().Icon(Icons.EllipsisVertical).Ghost().Small()
                        .WithDropDown(
                            new MenuItem("Open in File Manager", Icon: Icons.FolderOpen, Tag: "OpenInExplorer")
                                .OnSelect(() => PlatformHelper.OpenInFileManager(path, logger)),
                            new MenuItem("Open in Terminal", Icon: Icons.Terminal, Tag: "OpenInTerminal")
                                .OnSelect(() => PlatformHelper.OpenInTerminal(path, logger)),
                            new MenuItem($"Open in {config.Editor.Label}", Icon: Icons.Code, Tag: "OpenInEditor")
                                .OnSelect(() => config.OpenInEditor(path)),
                            new MenuItem("Copy Path to Clipboard", Icon: Icons.ClipboardCopy, Tag: "CopyPath")
                                .OnSelect(() => copyToClipboard(path))
                        )));
        }

        // Problematic commits warning
        var problematicCommits = gitData.CommitRows
            .Where(r => string.IsNullOrEmpty(r.Title) || r.FileCount == 0)
            .ToList();

        if (problematicCommits.Count > 0)
        {
            var warnings = problematicCommits.Select(r =>
            {
                if (string.IsNullOrEmpty(r.Title))
                    return $"`{r.ShortHash}` — commit not found or has no message";
                return $"`{r.ShortHash}` — commit has no file changes";
            });
            gitLayout |= Callout.Warning(
                string.Join("\n", warnings),
                "Potentially corrupted commits");
        }

        // Commits section
        if (plan.Commits.Count > 0)
        {
            gitLayout |= Text.Block("Commits").Bold();

            var commitTableRows = gitData.CommitRows.Select(row => new CommitTableRow(
                row.ShortHash,
                row.Hash,
                row.Title,
                row.FileCount?.ToString() ?? "–"
            )).ToList();

            gitLayout |= new TableBuilder<CommitTableRow>(commitTableRows)
                .Order(t => t.Commit, t => t.Message, t => t.Files)
                .Builder(t => t.Commit, f => f.Func<CommitTableRow, string>(shortHash =>
                    new Button(shortHash).Inline().OnClick(() =>
                    {
                        var row = commitTableRows.First(r => r.Commit == shortHash);
                        setOpenCommit(row.Hash);
                    })))
                .Remove(t => t.Hash);
        }

        // Pull requests section
        if (plan.Prs.Count > 0)
        {
            if (plan.Commits.Count > 0)
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
        if (plan.Commits.Count == 0 && plan.Prs.Count == 0 && gitData.WorktreeRows.Count == 0)
        {
            gitLayout |= Text.Muted("No worktrees, commits, or pull requests yet.");
        }

        return gitLayout;
    }

    private record WorktreeTableRow(string Name, string BranchCommit, string Actions);
    private record CommitTableRow(string Commit, string Hash, string Message, string Files);
    private record PrTableRow(string Repository, string Pr);
}
