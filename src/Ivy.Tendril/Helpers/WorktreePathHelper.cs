namespace Ivy.Tendril.Helpers;

/// <summary>
/// Helper for composing and analyzing worktree paths.
/// Centralizes worktree path logic to keep the worktree root inside the Windows process creation budget.
/// </summary>
internal static class WorktreePathHelper
{
    /// <summary>
    /// Maximum allowed worktree root length in characters.
    /// Derived from the .CMD ceiling (246) minus 1 for path separator minus 150 for documented launcher suffix allowance.
    /// This ensures spawned executables remain below the Windows MAX_PATH limit for process creation.
    /// </summary>
    internal const int MaxWorktreeRootLength = 95;

    /// <summary>
    /// Returns the path to the Worktrees directory for a given plan folder.
    /// </summary>
    public static string GetWorktreesDir(string planFolder)
    {
        return Path.Combine(planFolder, "Worktrees");
    }

    /// <summary>
    /// Returns the full worktree path for a given plan folder and repo path.
    /// </summary>
    public static string GetWorktreePath(string planFolder, string repoPath)
    {
        var relWorktreePath = GitHelper.DeriveWorktreeRelativePath(repoPath);
        return Path.Combine(planFolder, "Worktrees", relWorktreePath);
    }

    /// <summary>
    /// Attempts to recover the plan folder from a worktree path by walking up until finding a "Worktrees" ancestor.
    /// Handles both nested (Worktrees/owner/repo) and flat (Worktrees/repo) layouts.
    /// </summary>
    public static bool TryGetPlanFolderFromWorktree(string worktreePath, out string planFolder)
    {
        planFolder = string.Empty;
        var current = Path.GetFullPath(worktreePath);

        while (!string.IsNullOrEmpty(current))
        {
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || parent == current)
                break;

            var parentName = Path.GetFileName(parent);
            if (string.Equals(parentName, "Worktrees", StringComparison.OrdinalIgnoreCase))
            {
                planFolder = Path.GetDirectoryName(parent)!;
                return true;
            }

            current = parent;
        }

        return false;
    }

    /// <summary>
    /// Computes the worst-case worktree root length for the given plans root and relative repo path.
    /// The worktree root is the path before any build tool spawns executables.
    /// </summary>
    /// <param name="plansRootLength">Length of the plans root directory path</param>
    /// <param name="relativeRepoPathLength">Length of the relative repo path (e.g., "owner/repo")</param>
    /// <param name="folderNameLength">Optional folder name length; defaults to worst-case folder name length</param>
    public static int WorstCaseWorktreeRootLength(int plansRootLength, int relativeRepoPathLength, int? folderNameLength = null)
    {
        var maxFolderLength = folderNameLength ?? (5 + 1 + PlanYamlHelper.SafeTitleMaxLength);
        return plansRootLength + 1 + maxFolderLength + 1 + "Worktrees".Length + 1 + relativeRepoPathLength;
    }
}
