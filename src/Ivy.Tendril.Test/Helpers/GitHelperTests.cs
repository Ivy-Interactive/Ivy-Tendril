using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Test.Helpers;

public class GitHelperTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "GitHelperTests_" + Guid.NewGuid().ToString("N")[..8]);

    public GitHelperTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void ResolveRepoRootFromWorktree_NoGitFile_ReturnsNull()
    {
        var result = GitHelper.ResolveRepoRootFromWorktree(_tempDir);
        Assert.Null(result);
    }

    [Fact]
    public void ResolveRepoRootFromWorktree_GitFileWithInvalidContent_ReturnsNull()
    {
        File.WriteAllText(Path.Combine(_tempDir, ".git"), "not a valid gitdir reference");

        var result = GitHelper.ResolveRepoRootFromWorktree(_tempDir);
        Assert.Null(result);
    }

    [Fact]
    public void ResolveRepoRootFromWorktree_GitFilePointingToValidWorktree_ReturnsRepoRoot()
    {
        // Simulate: repo at _tempDir/repo with .git/worktrees/feature/
        var repoDir = Path.Combine(_tempDir, "repo");
        var gitDir = Path.Combine(repoDir, ".git");
        var worktreesDir = Path.Combine(gitDir, "worktrees", "feature");
        Directory.CreateDirectory(worktreesDir);

        // The worktree directory with a .git file pointing back
        var wtDir = Path.Combine(_tempDir, "worktree");
        Directory.CreateDirectory(wtDir);
        var relativePath = Path.GetRelativePath(wtDir, worktreesDir);
        File.WriteAllText(Path.Combine(wtDir, ".git"), $"gitdir: {relativePath}");

        var result = GitHelper.ResolveRepoRootFromWorktree(wtDir);

        Assert.Equal(repoDir, result);
    }

    [Fact]
    public void ResolveRepoRootFromWorktree_AbsoluteGitDir_ReturnsRepoRoot()
    {
        var repoDir = Path.Combine(_tempDir, "myrepo");
        var gitDir = Path.Combine(repoDir, ".git");
        var worktreesDir = Path.Combine(gitDir, "worktrees", "branch1");
        Directory.CreateDirectory(worktreesDir);

        var wtDir = Path.Combine(_tempDir, "wt");
        Directory.CreateDirectory(wtDir);
        File.WriteAllText(Path.Combine(wtDir, ".git"), $"gitdir: {worktreesDir}");

        var result = GitHelper.ResolveRepoRootFromWorktree(wtDir);

        Assert.Equal(repoDir, result);
    }

    [Fact]
    public void ResolveRepoRootFromWorktree_RepoRootDoesNotExist_ReturnsNull()
    {
        var wtDir = Path.Combine(_tempDir, "orphan-wt");
        Directory.CreateDirectory(wtDir);
        // Point to a non-existent directory structure
        File.WriteAllText(Path.Combine(wtDir, ".git"), "gitdir: /nonexistent/path/.git/worktrees/x");

        var result = GitHelper.ResolveRepoRootFromWorktree(wtDir);

        Assert.Null(result);
    }
}
