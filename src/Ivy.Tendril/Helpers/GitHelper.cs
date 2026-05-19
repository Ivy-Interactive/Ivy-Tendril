using System.Text.RegularExpressions;

namespace Ivy.Tendril.Helpers;

public static class GitHelper
{
    public static string? ResolveRepoRootFromWorktree(string wtDir)
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
}
