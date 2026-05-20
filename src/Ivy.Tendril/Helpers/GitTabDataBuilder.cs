using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Helpers;

public static class GitTabDataBuilder
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
        var worktreesDir = Path.Combine(plan.FolderPath, "Worktrees");

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
                mainWorktree.CommitHash.Length > 7 ? mainWorktree.CommitHash[..7] : mainWorktree.CommitHash
            );
        }

        var resolvedRoot = GitHelper.ResolveRepoRootFromWorktree(repoDir);
        if (resolvedRoot == null) return (null, null, null);

        var parentWorktrees = gitService.GetWorktrees(resolvedRoot);
        if (!parentWorktrees.IsSuccess || parentWorktrees.Value == null) return (resolvedRoot, null, null);

        var main = parentWorktrees.Value.FirstOrDefault(w =>
            Path.GetFullPath(w.Path).Equals(Path.GetFullPath(resolvedRoot), StringComparison.OrdinalIgnoreCase));

        if (main == null) return (resolvedRoot, null, null);

        return (
            resolvedRoot,
            main.Branch,
            main.CommitHash.Length > 7 ? main.CommitHash[..7] : main.CommitHash
        );
    }
}
