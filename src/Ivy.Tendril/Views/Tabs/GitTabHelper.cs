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
            var worktrees = gitService.GetWorktrees(repoDir);
            if (worktrees == null) continue;

            // Find the worktree that matches this directory (not the main repo)
            var worktree = worktrees.FirstOrDefault(w =>
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

        // Worktrees section (new)
        if (gitData.WorktreeRows.Count > 0)
        {
            gitLayout |= Text.Block("Worktrees").Bold();
            var worktreesTable = new Table(
                new TableRow(
                    new TableCell("Name").IsHeader(),
                    new TableCell("Branch:Commit").IsHeader(),
                    new TableCell("Actions").IsHeader()
                )
                { IsHeader = true }
            );

            foreach (var row in gitData.WorktreeRows)
            {
                var pathCapture = row.Path;
                var actionsMenu = new Button().Icon(Icons.EllipsisVertical).Ghost().Small()
                    .WithDropDown(
                        new MenuItem("Open in File Manager", Icon: Icons.FolderOpen, Tag: "OpenInExplorer")
                            .OnSelect(() => PlatformHelper.OpenInFileManager(pathCapture, logger)),
                        new MenuItem("Open in Terminal", Icon: Icons.Terminal, Tag: "OpenInTerminal")
                            .OnSelect(() => PlatformHelper.OpenInTerminal(pathCapture, logger)),
                        new MenuItem($"Open in {config.Editor.Label}", Icon: Icons.Code, Tag: "OpenInEditor")
                            .OnSelect(() => config.OpenInEditor(pathCapture)),
                        new MenuItem("Copy Path to Clipboard", Icon: Icons.ClipboardCopy, Tag: "CopyPath")
                            .OnSelect(() => copyToClipboard(pathCapture))
                    );

                worktreesTable |= new TableRow(
                    new TableCell(row.Name),
                    new TableCell($"{row.Branch}:{row.ShortHash}"),
                    new TableCell(actionsMenu)
                );
            }

            gitLayout |= worktreesTable;
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
            var commitsTable = new Table(
                new TableRow(
                    new TableCell("Commit").IsHeader(),
                    new TableCell("Message").IsHeader(),
                    new TableCell("Files").IsHeader()
                )
                { IsHeader = true }
            );

            foreach (var row in gitData.CommitRows)
                commitsTable |= new TableRow(
                    new TableCell(new Button(row.ShortHash).Inline().OnClick(() => setOpenCommit(row.Hash))),
                    new TableCell(row.Title),
                    new TableCell(row.FileCount?.ToString() ?? "–")
                );

            gitLayout |= commitsTable;
        }

        // Pull requests section
        if (plan.Prs.Count > 0)
        {
            if (plan.Commits.Count > 0)
                gitLayout |= new Separator();

            gitLayout |= Text.Block("Pull Requests").Bold();
            var prsTable = new Table(
                new TableRow(
                    new TableCell("Repository").IsHeader(),
                    new TableCell("PR").IsHeader()
                )
                { IsHeader = true }
            );

            foreach (var pr in plan.Prs.Where(PullRequestApp.IsValidUrl))
            {
                var prCapture = pr;
                prsTable |= new TableRow(
                    new TableCell(PullRequestApp.ExtractRepo(pr)),
                    new TableCell(new Button(pr).Link().OnClick(() => client.OpenUrl(prCapture)))
                );
            }

            gitLayout |= prsTable;
        }

        // Empty state
        if (plan.Commits.Count == 0 && plan.Prs.Count == 0 && gitData.WorktreeRows.Count == 0)
        {
            gitLayout |= Text.Muted("No worktrees, commits, or pull requests yet.");
        }

        return gitLayout;
    }
}
