using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Helpers;

public static class GitTabDataBuilder
{
    public record GitTabData(
        List<WorktreeSection> WorktreeSections,
        List<PlanContentHelpers.CommitRow> UnassociatedCommitRows,
        // Ref status for the unassociated commits only. Commits listed under a worktree section are
        // ancestors of that worktree's HEAD and so reachable by definition; the unassociated ones
        // are those no surviving worktree accounts for, which is exactly where a commit can turn
        // out to be reachable from nothing at all.
        Dictionary<string, CommitRefStatus>? UnassociatedCommitRefStatus = null
    );

    public record WorktreeSection(
        string Name,
        string Path,
        string Branch,
        string ShortHash,
        bool HasUncommittedChanges,
        List<PlanContentHelpers.CommitRow> CommitRows,
        string? ParentRepoPath = null,
        string? BaseBranch = null,
        string? BaseShortHash = null
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

    /// <summary>
    /// Number of git items behind the Git tab: worktrees, recorded commits and PRs.
    /// Zero means there is nothing to show and the tab is hidden.
    /// </summary>
    public static int CountGitItems(GitTabData data, PlanFile plan) =>
        data.WorktreeSections.Count + plan.Commits.Count + plan.Prs.Count;

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

        return new GitTabData(sections, unassigned, ResolveRefStatus(plan, config, gitService, unassigned));
    }

    /// <summary>
    ///     Asks each of the plan's repos whether it still holds the given commits, and keeps the best
    ///     answer per commit: a commit lives in exactly one of a multi-repo plan's repos, so a repo
    ///     that has never heard of it must not mask the repo that has.
    /// </summary>
    private static Dictionary<string, CommitRefStatus> ResolveRefStatus(
        PlanFile plan, IConfigService config, IGitService gitService, List<PlanContentHelpers.CommitRow> rows)
    {
        var statuses = new Dictionary<string, CommitRefStatus>(StringComparer.OrdinalIgnoreCase);
        if (rows.Count == 0) return statuses;

        var hashes = rows.Select(r => r.Hash).ToList();
        foreach (var repo in plan.GetEffectiveRepoPaths(config))
        {
            var result = gitService.GetCommitRefStatus(repo, hashes);
            if (!result.IsSuccess || result.Value == null) continue;

            foreach (var (hash, status) in result.Value)
            {
                if (!statuses.TryGetValue(hash, out var current) || Rank(status) < Rank(current))
                    statuses[hash] = status;
            }
        }

        return statuses;

        // Reachable is the most informative answer, Missing the least.
        static int Rank(CommitRefStatus status) => status switch
        {
            CommitRefStatus.Reachable => 0,
            CommitRefStatus.Unreachable => 1,
            _ => 2
        };
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

        var shortHash = Shorten(worktree.CommitHash);

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

        var (parentRepoPath, baseBranch, baseShortHash) = ResolveParentInfo(repoDir, worktreesResult.Value, gitService);

        return new WorktreeSection(
            Path.GetFileName(repoDir),
            repoDir,
            worktree.Branch,
            shortHash,
            hasUncommitted,
            worktreeCommits,
            parentRepoPath,
            baseBranch,
            baseShortHash
        );
    }

    private static (string? Path, string? Branch, string? ShortHash) ResolveParentInfo(
        string repoDir, List<WorktreeInfo> worktrees, IGitService gitService)
    {
        var fallback = ResolveMainWorktreeInfo(repoDir, worktrees, gitService);

        var baseResult = gitService.GetWorktreeBase(repoDir);
        if (baseResult.IsSuccess && baseResult.Value != null)
        {
            var baseInfo = baseResult.Value;
            return (fallback.Path, StripOriginPrefix(baseInfo.Branch), Shorten(baseInfo.CommitHash));
        }

        return fallback;
    }

    private static (string? Path, string? Branch, string? ShortHash) ResolveMainWorktreeInfo(
        string repoDir, List<WorktreeInfo> worktrees, IGitService gitService)
    {
        var mainWorktree = worktrees.FirstOrDefault(w =>
            !Path.GetFullPath(w.Path).Equals(Path.GetFullPath(repoDir), StringComparison.OrdinalIgnoreCase));

        if (mainWorktree != null)
        {
            return (
                mainWorktree.Path,
                mainWorktree.Branch,
                Shorten(mainWorktree.CommitHash)
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
            Shorten(main.CommitHash)
        );
    }

    private static string StripOriginPrefix(string branch) =>
        branch.StartsWith("origin/", StringComparison.Ordinal) ? branch["origin/".Length..] : branch;

    private static string Shorten(string hash) => hash.Length > 7 ? hash[..7] : hash;
}
