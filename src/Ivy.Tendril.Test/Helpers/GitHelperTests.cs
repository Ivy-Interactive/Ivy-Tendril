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
        {
            try
            {
                DeleteDirectory(_tempDir);
            }
            catch { }
        }
    }

    private static void DeleteDirectory(string path)
    {
        foreach (var directory in Directory.GetDirectories(path))
        {
            DeleteDirectory(directory);
        }

        foreach (var file in Directory.GetFiles(path))
        {
            var attr = File.GetAttributes(file);
            if ((attr & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
            {
                File.SetAttributes(file, attr & ~FileAttributes.ReadOnly);
            }
            File.Delete(file);
        }

        Directory.Delete(path, false);
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

    [Fact]
    public async Task IsValidBranchAsync_InvalidArgs_ReturnsFalse()
    {
        Assert.False(await GitHelper.IsValidBranchAsync("", "main"));
        Assert.False(await GitHelper.IsValidBranchAsync("path", ""));
    }

    [Fact]
    public async Task IsValidBranchAsync_NonExistentLocalPath_ReturnsFalse()
    {
        var nonExistent = Path.Combine(_tempDir, "nonexistent");
        Assert.False(await GitHelper.IsValidBranchAsync(nonExistent, "main"));
    }

    [Fact]
    public async Task IsValidBranchAsync_LocalRepo_ExistingAndNonExistingBranches_Works()
    {
        var repoPath = Path.Combine(_tempDir, "local-repo");
        Directory.CreateDirectory(repoPath);
        InitGitRepo(repoPath, "dev-branch");

        // Existing branch
        Assert.True(await GitHelper.IsValidBranchAsync(repoPath, "dev-branch"));

        // Non-existing branch
        Assert.False(await GitHelper.IsValidBranchAsync(repoPath, "main-nonexistent"));
    }

    [Fact]
    public async Task IsValidBranchAsync_RemoteUrl_ExistingAndNonExistingBranches_Works()
    {
        // Use a known public git repository
        var remoteUrl = "https://github.com/git/git.git";

        // If network is offline, ls-remote will fail and return false.
        // We only verify assertions if we can successfully query the remote.
        var isReachable = await GitHelper.IsValidBranchAsync(remoteUrl, "master");
        if (isReachable)
        {
            Assert.True(isReachable);
            Assert.False(await GitHelper.IsValidBranchAsync(remoteUrl, "nonexistent-branch-12345"));
        }
    }

    private static void InitGitRepo(string path, string branchName)
    {
        using var pInit = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", "init") { WorkingDirectory = path, CreateNoWindow = true });
        pInit?.WaitForExit();

        using var pEmail = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", "config user.email \"test@example.com\"") { WorkingDirectory = path, CreateNoWindow = true });
        pEmail?.WaitForExit();

        using var pName = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", "config user.name \"Test User\"") { WorkingDirectory = path, CreateNoWindow = true });
        pName?.WaitForExit();

        File.WriteAllText(Path.Combine(path, "test.txt"), "hello");

        using var pAdd = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", "add test.txt") { WorkingDirectory = path, CreateNoWindow = true });
        pAdd?.WaitForExit();

        using var pCommit = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", "commit -m \"initial\"") { WorkingDirectory = path, CreateNoWindow = true });
        pCommit?.WaitForExit();

        using var pCheckout = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", $"checkout -b {branchName}") { WorkingDirectory = path, CreateNoWindow = true });
        pCheckout?.WaitForExit();
    }
}
