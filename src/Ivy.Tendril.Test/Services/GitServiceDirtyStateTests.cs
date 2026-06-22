using System.Diagnostics;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Git;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test.Services;

public class GitServiceDirtyStateTests : IDisposable
{
    private readonly string _testRepoPath;
    private readonly string _bareRepoPath;
    private readonly IConfigService _configService;

    public GitServiceDirtyStateTests()
    {
        _bareRepoPath = Path.Combine(Path.GetTempPath(), $"git-bare-{Guid.NewGuid()}");
        _testRepoPath = Path.Combine(Path.GetTempPath(), $"git-dirty-{Guid.NewGuid()}");

        InitializeReposWithRemote();

        _configService = CreateMockConfigService();
    }

    public void Dispose()
    {
        TryDeleteDirectory(_testRepoPath);
        TryDeleteDirectory(_bareRepoPath);
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        try { Directory.Delete(path, true); } catch { }
    }

    private void InitializeReposWithRemote()
    {
        Directory.CreateDirectory(_bareRepoPath);
        RunGitAt(_bareRepoPath, "init --bare");

        RunGitAt(Path.GetTempPath(), $"clone \"{_bareRepoPath}\" \"{_testRepoPath}\"");

        RunGit("config user.email test@example.com");
        RunGit("config user.name TestUser");

        File.WriteAllText(Path.Combine(_testRepoPath, "file1.txt"), "Initial content");
        RunGit("add file1.txt");
        RunGit("commit -m \"Initial commit\"");
        RunGit("push origin master");
    }

    private void RunGit(string args) => RunGitAt(_testRepoPath, args);

    private void RunGitAt(string workDir, string args)
    {
        var psi = new ProcessStartInfo("git", args)
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi);
        process?.StandardOutput.ReadToEnd();
        process?.StandardError.ReadToEnd();
        process?.WaitForExit(10000);
    }

    private string RunGitOutput(string args)
    {
        var psi = new ProcessStartInfo("git", args)
        {
            WorkingDirectory = _testRepoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi);
        var output = process?.StandardOutput.ReadToEnd() ?? "";
        process?.WaitForExit(10000);
        return output.Trim();
    }

    private IConfigService CreateMockConfigService()
    {
        var config = new ConfigService(new TendrilSettings());
        var tempDir = Path.Combine(Path.GetTempPath(), $"tendril-config-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(Path.Combine(tempDir, "Inbox"));
        Directory.CreateDirectory(Path.Combine(tempDir, "Plans"));
        File.WriteAllText(Path.Combine(tempDir, "config.yaml"), "gitTimeout: 30\n");
        config.SetTendrilHome(tempDir);
        return config;
    }

    private GitService CreateService() => new(_configService, NullLogger<GitService>.Instance);

    // --- Clean state ---

    [Fact]
    public void CleanRepo_ReturnsNotDirty()
    {
        var service = CreateService();

        var result = service.GetRepoDirtyState(_testRepoPath, "master");

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsDirty);
        Assert.Empty(result.Value!.Reasons);
    }

    // --- Branch checks ---

    [Fact]
    public void WrongBranch_ReturnsDirty()
    {
        RunGit("checkout -b feature-branch");
        var service = CreateService();

        var result = service.GetRepoDirtyState(_testRepoPath, "master");

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsDirty);
        Assert.Contains(result.Value!.Reasons, r => r.Reason == DirtyReason.NotOnExpectedBranch);
    }

    [Fact]
    public void DetachedHead_ReturnsDirty()
    {
        var hash = RunGitOutput("rev-parse HEAD");
        RunGit($"checkout {hash}");
        var service = CreateService();

        var result = service.GetRepoDirtyState(_testRepoPath, "master");

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsDirty);
        Assert.Contains(result.Value!.Reasons, r => r.Reason == DirtyReason.DetachedHead);
    }

    // --- Ahead of origin ---

    [Fact]
    public void AheadOfOrigin_ReturnsDirty()
    {
        File.WriteAllText(Path.Combine(_testRepoPath, "file2.txt"), "new file");
        RunGit("add file2.txt");
        RunGit("commit -m \"Local commit\"");
        var service = CreateService();

        var result = service.GetRepoDirtyState(_testRepoPath, "master");

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsDirty);
        var reason = Assert.Single(result.Value!.Reasons, r => r.Reason == DirtyReason.AheadOfOrigin);
        // AheadOfOrigin reports the unpushed commits (not files) — matches DirtyRepoDialog,
        // which renders reason.Commits for this reason and reason.Files for the others.
        Assert.NotEmpty(reason.Commits);
    }

    [Fact]
    public void BehindOrigin_IsNotDirty()
    {
        // Push a commit from a second clone, so origin is ahead
        var secondClone = Path.Combine(Path.GetTempPath(), $"git-clone2-{Guid.NewGuid()}");
        try
        {
            RunGitAt(Path.GetTempPath(), $"clone \"{_bareRepoPath}\" \"{secondClone}\"");
            RunGitAt(secondClone, "config user.email test@example.com");
            RunGitAt(secondClone, "config user.name TestUser");
            File.WriteAllText(Path.Combine(secondClone, "extra.txt"), "extra");
            RunGitAt(secondClone, "add extra.txt");
            RunGitAt(secondClone, "commit -m \"Extra commit\"");
            RunGitAt(secondClone, "push origin master");

            RunGit("fetch origin");
            var service = CreateService();

            var result = service.GetRepoDirtyState(_testRepoPath, "master");

            Assert.True(result.IsSuccess);
            Assert.False(result.Value!.IsDirty);
        }
        finally
        {
            TryDeleteDirectory(secondClone);
        }
    }

    // --- Uncommitted changes ---

    [Fact]
    public void StagedChanges_ReturnsDirty()
    {
        File.WriteAllText(Path.Combine(_testRepoPath, "file1.txt"), "modified");
        RunGit("add file1.txt");
        var service = CreateService();

        var result = service.GetRepoDirtyState(_testRepoPath, "master");

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsDirty);
        var reason = Assert.Single(result.Value!.Reasons, r => r.Reason == DirtyReason.UncommittedChanges);
        Assert.Contains("file1.txt", reason.Files);
    }

    [Fact]
    public void UnstagedChanges_ReturnsDirty()
    {
        File.WriteAllText(Path.Combine(_testRepoPath, "file1.txt"), "unstaged change");
        var service = CreateService();

        var result = service.GetRepoDirtyState(_testRepoPath, "master");

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsDirty);
        var reason = Assert.Single(result.Value!.Reasons, r => r.Reason == DirtyReason.UncommittedChanges);
        Assert.Contains("file1.txt", reason.Files);
    }

    [Fact]
    public void GitignoredFiles_AreNotDirty()
    {
        File.WriteAllText(Path.Combine(_testRepoPath, ".gitignore"), "ignored.txt\n");
        RunGit("add .gitignore");
        RunGit("commit -m \"Add gitignore\"");
        RunGit("push origin master");
        File.WriteAllText(Path.Combine(_testRepoPath, "ignored.txt"), "should not count");
        var service = CreateService();

        var result = service.GetRepoDirtyState(_testRepoPath, "master");

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsDirty);
    }

    // --- Untracked files ---

    [Fact]
    public void UntrackedFile_ReturnsDirty()
    {
        File.WriteAllText(Path.Combine(_testRepoPath, "newfile.txt"), "untracked");
        var service = CreateService();

        var result = service.GetRepoDirtyState(_testRepoPath, "master");

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsDirty);
        var reason = Assert.Single(result.Value!.Reasons, r => r.Reason == DirtyReason.UntrackedFiles);
        Assert.Contains("newfile.txt", reason.Files);
    }

    [Fact]
    public void UntrackedGitignoredFile_IsNotDirty()
    {
        File.WriteAllText(Path.Combine(_testRepoPath, ".gitignore"), "*.log\n");
        RunGit("add .gitignore");
        RunGit("commit -m \"Add gitignore\"");
        RunGit("push origin master");
        File.WriteAllText(Path.Combine(_testRepoPath, "debug.log"), "log content");
        var service = CreateService();

        var result = service.GetRepoDirtyState(_testRepoPath, "master");

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsDirty);
    }

    // --- In-progress operations ---

    [Fact]
    public void MidMerge_ReturnsDirty()
    {
        // Create a conflicting branch
        RunGit("checkout -b conflict-branch");
        File.WriteAllText(Path.Combine(_testRepoPath, "file1.txt"), "conflict version A");
        RunGit("add file1.txt");
        RunGit("commit -m \"Conflict A\"");

        RunGit("checkout master");
        File.WriteAllText(Path.Combine(_testRepoPath, "file1.txt"), "conflict version B");
        RunGit("add file1.txt");
        RunGit("commit -m \"Conflict B\"");
        RunGit("push origin master");

        // Start merge that will conflict
        RunGit("merge conflict-branch");

        var service = CreateService();
        var result = service.GetRepoDirtyState(_testRepoPath, "master");

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsDirty);
        Assert.Contains(result.Value!.Reasons, r => r.Reason == DirtyReason.InProgressOperation);
    }

    [Fact]
    public void MidRebase_ReturnsDirty()
    {
        var gitDir = Path.Combine(_testRepoPath, ".git", "rebase-merge");
        Directory.CreateDirectory(gitDir);
        var service = CreateService();

        var result = service.GetRepoDirtyState(_testRepoPath, "master");

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsDirty);
        Assert.Contains(result.Value!.Reasons, r => r.Reason == DirtyReason.InProgressOperation);

        Directory.Delete(gitDir, true);
    }

    // --- No remote ---

    [Fact]
    public void NoRemote_ReturnsDirty()
    {
        var isolatedRepo = Path.Combine(Path.GetTempPath(), $"git-noremote-{Guid.NewGuid()}");
        try
        {
            Directory.CreateDirectory(isolatedRepo);
            RunGitAt(isolatedRepo, "init");
            RunGitAt(isolatedRepo, "config user.email test@example.com");
            RunGitAt(isolatedRepo, "config user.name TestUser");
            File.WriteAllText(Path.Combine(isolatedRepo, "f.txt"), "content");
            RunGitAt(isolatedRepo, "add f.txt");
            RunGitAt(isolatedRepo, "commit -m \"init\"");

            var service = CreateService();
            var result = service.GetRepoDirtyState(isolatedRepo, "master");

            Assert.True(result.IsSuccess);
            Assert.True(result.Value!.IsDirty);
            Assert.Contains(result.Value!.Reasons, r => r.Reason == DirtyReason.NoRemoteConfigured);
        }
        finally
        {
            TryDeleteDirectory(isolatedRepo);
        }
    }

    // --- Multiple reasons ---

    [Fact]
    public void MultipleReasons_CollectsAll()
    {
        // Wrong branch + uncommitted changes + untracked file
        RunGit("checkout -b other-branch");
        File.WriteAllText(Path.Combine(_testRepoPath, "file1.txt"), "modified");
        RunGit("add file1.txt");
        File.WriteAllText(Path.Combine(_testRepoPath, "untracked.txt"), "new");
        var service = CreateService();

        var result = service.GetRepoDirtyState(_testRepoPath, "master");

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsDirty);
        Assert.Contains(result.Value!.Reasons, r => r.Reason == DirtyReason.NotOnExpectedBranch);
        Assert.Contains(result.Value!.Reasons, r => r.Reason == DirtyReason.UncommittedChanges);
        Assert.Contains(result.Value!.Reasons, r => r.Reason == DirtyReason.UntrackedFiles);
    }

    // --- Error cases ---

    [Fact]
    public void NonExistentPath_ReturnsFailure()
    {
        var service = CreateService();

        var result = service.GetRepoDirtyState("/nonexistent/path/abc123", "master");

        Assert.False(result.IsSuccess);
        Assert.Equal(GitError.InvalidRepoPath, result.Error);
    }

    [Fact]
    public void NotAGitRepo_ReturnsFailure()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"not-a-repo-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var service = CreateService();

            var result = service.GetRepoDirtyState(tempDir, "master");

            Assert.False(result.IsSuccess);
            Assert.Equal(GitError.InvalidRepoPath, result.Error);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }
}
