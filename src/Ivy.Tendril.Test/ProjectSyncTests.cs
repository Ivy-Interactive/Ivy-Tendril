using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Ivy.Tendril.Commands;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ivy.Tendril.Test;

[Collection("TendrilHome")]
public class ProjectSyncTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("tendril-sync-test");
    private readonly string _bareRepoPath;
    private readonly string _testRepoPath;
    private readonly string _upstreamRepoPath;
    private readonly string _originalTendrilHome;

    public ProjectSyncTests()
    {
        _originalTendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? "";
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _tempDir.Path);

        _bareRepoPath = Path.Combine(_tempDir.Path, "bare.git");
        _testRepoPath = Path.Combine(_tempDir.Path, "test-repo");
        _upstreamRepoPath = Path.Combine(_tempDir.Path, "upstream-repo");

        InitializeGitRepos();

        var configYaml = $@"
projects:
  - name: TestProject
    repos:
      - path: {_testRepoPath.Replace('\\', '/')}
        baseBranch: main
verifications: []
";
        File.WriteAllText(Path.Combine(_tempDir.Path, "config.yaml"), configYaml);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _originalTendrilHome);
        _tempDir.Dispose();
    }

    private void InitializeGitRepos()
    {
        Directory.CreateDirectory(_bareRepoPath);
        RunGitAt(_bareRepoPath, "init --bare");

        Directory.CreateDirectory(_testRepoPath);
        RunGitAt(_testRepoPath, "init");
        RunGitAt(_testRepoPath, $"remote add origin \"{_bareRepoPath}\"");
        RunGitAt(_testRepoPath, "config user.email test@example.com");
        RunGitAt(_testRepoPath, "config user.name TestUser");
        RunGitAt(_testRepoPath, "checkout -b main");

        File.WriteAllText(Path.Combine(_testRepoPath, "file1.txt"), "Initial content");
        RunGitAt(_testRepoPath, "add file1.txt");
        RunGitAt(_testRepoPath, "commit -m \"Initial commit\"");
        RunGitAt(_testRepoPath, "push -u origin main");

        // Clone upstream copy to easily push new commits from remote
        RunGitAt(_tempDir.Path, $"clone \"{_bareRepoPath}\" \"{_upstreamRepoPath}\"");
        RunGitAt(_upstreamRepoPath, "config user.email upstream@example.com");
        RunGitAt(_upstreamRepoPath, "config user.name UpstreamUser");
    }

    private static void RunGitAt(string workDir, string args)
    {
        var (exitCode, stdout, stderr) = GitHelper.RunGit(args, workDir, 30000);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"git {args} in {workDir} failed with code {exitCode}: {stderr} {stdout}");
        }
    }

    [Fact]
    public async Task ProjectSyncHelper_CleanRepo_FastForwardsBaseBranch()
    {
        // Push a new commit from upstream
        File.WriteAllText(Path.Combine(_upstreamRepoPath, "file2.txt"), "Upstream file");
        RunGitAt(_upstreamRepoPath, "add file2.txt");
        RunGitAt(_upstreamRepoPath, "commit -m \"Add file2 from upstream\"");
        RunGitAt(_upstreamRepoPath, "push origin main");

        var result = await ProjectSyncHelper.SyncRepositoryAsync(_testRepoPath, "main", _tempDir.Path);

        Assert.True(result.Success);
        Assert.Contains("Fast-forwarded", result.Message);
        Assert.True(File.Exists(Path.Combine(_testRepoPath, "file2.txt")));
        Assert.Equal("Upstream file", File.ReadAllText(Path.Combine(_testRepoPath, "file2.txt")));
    }

    [Fact]
    public async Task ProjectSyncHelper_AlreadyUpToDate_SucceedsWithoutChanges()
    {
        var result = await ProjectSyncHelper.SyncRepositoryAsync(_testRepoPath, "main", _tempDir.Path);

        Assert.True(result.Success);
        Assert.Equal("Already up to date.", result.Message);
        Assert.False(result.CanFixWithAgent);
    }

    [Fact]
    public async Task ProjectSyncHelper_DirtyWorkingTree_FailsSafelyWithoutReset()
    {
        // Make working tree dirty
        var dirtyPath = Path.Combine(_testRepoPath, "file1.txt");
        File.WriteAllText(dirtyPath, "Modified uncommitted content");

        // Also push upstream commit so sync has something to pull
        File.WriteAllText(Path.Combine(_upstreamRepoPath, "file3.txt"), "Upstream file 3");
        RunGitAt(_upstreamRepoPath, "add file3.txt");
        RunGitAt(_upstreamRepoPath, "commit -m \"Add file3 from upstream\"");
        RunGitAt(_upstreamRepoPath, "push origin main");

        var result = await ProjectSyncHelper.SyncRepositoryAsync(_testRepoPath, "main", _tempDir.Path);

        Assert.False(result.Success);
        Assert.True(result.CanFixWithAgent);
        Assert.Contains("uncommitted", result.Message, StringComparison.OrdinalIgnoreCase);

        // Verify uncommitted work was NOT lost (no destructive reset)
        Assert.Equal("Modified uncommitted content", File.ReadAllText(dirtyPath));
    }

    [Fact]
    public async Task ProjectSyncHelper_DivergedBranch_FailsSafely()
    {
        // Commit locally in test repo
        File.WriteAllText(Path.Combine(_testRepoPath, "local_diverge.txt"), "Local content");
        RunGitAt(_testRepoPath, "add local_diverge.txt");
        RunGitAt(_testRepoPath, "commit -m \"Local diverging commit\"");

        // Push diverging commit from upstream
        File.WriteAllText(Path.Combine(_upstreamRepoPath, "upstream_diverge.txt"), "Upstream content");
        RunGitAt(_upstreamRepoPath, "add upstream_diverge.txt");
        RunGitAt(_upstreamRepoPath, "commit -m \"Upstream diverging commit\"");
        RunGitAt(_upstreamRepoPath, "push origin main");

        var result = await ProjectSyncHelper.SyncRepositoryAsync(_testRepoPath, "main", _tempDir.Path);

        Assert.False(result.Success);
        Assert.True(result.CanFixWithAgent);
        Assert.NotNull(result.GitErrorDetails);
    }

    [Fact]
    public async Task ProjectSyncHelper_FetchFailure_ReturnsClearError()
    {
        var invalidRemoteRepo = Path.Combine(_tempDir.Path, "invalid-remote-repo");
        Directory.CreateDirectory(invalidRemoteRepo);
        RunGitAt(invalidRemoteRepo, "init");
        RunGitAt(invalidRemoteRepo, "config user.email test@example.com");
        RunGitAt(invalidRemoteRepo, "config user.name TestUser");
        RunGitAt(invalidRemoteRepo, "checkout -b main");
        File.WriteAllText(Path.Combine(invalidRemoteRepo, "f.txt"), "1");
        RunGitAt(invalidRemoteRepo, "add f.txt");
        RunGitAt(invalidRemoteRepo, "commit -m \"c\"");
        RunGitAt(invalidRemoteRepo, "remote add origin \"/nonexistent/directory/path/that/cannot/exist\"");

        var result = await ProjectSyncHelper.SyncRepositoryAsync(invalidRemoteRepo, "main", _tempDir.Path);

        Assert.False(result.Success);
        Assert.True(result.CanFixWithAgent);
        Assert.Contains("Failed to fetch", result.Message);
        Assert.NotNull(result.GitErrorDetails);
    }

    [Fact]
    public void ProjectSyncHelper_GenerateDiagnosticPrompt_ContainsRepoContext()
    {
        var result = new ProjectSyncResult
        {
            RepoPath = "/Users/test/my-repo",
            BaseBranch = "development",
            GitErrorDetails = "error: Your local changes would be overwritten by merge.",
            Message = "Fast-forward merge failed",
            CanFixWithAgent = true
        };

        var prompt = ProjectSyncHelper.GenerateDiagnosticPrompt(result);

        Assert.Contains("/Users/test/my-repo", prompt);
        Assert.Contains("development", prompt);
        Assert.Contains("error: Your local changes would be overwritten by merge.", prompt);
        Assert.Contains("Please inspect the repository status", prompt);
    }

    [Fact]
    public void ProjectSyncCommand_ValidProject_ExecutesSafeSync()
    {
        var config = new ConfigService();
        var cliServices = new ServiceCollection();
        cliServices.AddSingleton<IConfigService>(config);
        cliServices.AddSingleton<ConfigService>(config);

        var app = Program.ConfigureCliCommands(cliServices);

        // First sync on clean repo should succeed
        var exitCode = app.Run(["project", "sync", "TestProject"]);
        Assert.Equal(0, exitCode);

        // Now dirty the repo
        File.WriteAllText(Path.Combine(_testRepoPath, "untracked.txt"), "untracked");

        // Second sync on dirty repo should fail safely and return non-zero exit code
        var failExitCode = app.Run(["project", "sync", "TestProject"]);
        Assert.Equal(1, failExitCode);
    }
}
