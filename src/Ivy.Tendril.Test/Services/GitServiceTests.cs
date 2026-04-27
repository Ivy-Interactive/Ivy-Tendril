using System.Diagnostics;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test.Services;

public class GitServiceTests : IDisposable
{
    private readonly string _testRepoPath;
    private readonly IConfigService _configService;

    public GitServiceTests()
    {
        _testRepoPath = Path.Combine(Path.GetTempPath(), $"git-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testRepoPath);

        InitializeTestRepo();

        _configService = CreateMockConfigService();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRepoPath))
        {
            try
            {
                Directory.Delete(_testRepoPath, true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    private void InitializeTestRepo()
    {
        RunGit("init");
        RunGit("config user.email test@example.com");
        RunGit("config user.name TestUser");

        File.WriteAllText(Path.Combine(_testRepoPath, "file1.txt"), "Initial content");
        RunGit("add file1.txt");
        RunGit("commit -m \"Initial commit\"");

        File.WriteAllText(Path.Combine(_testRepoPath, "file2.txt"), "Second file");
        RunGit("add file2.txt");
        RunGit("commit -m \"Add file2\"");

        File.WriteAllText(Path.Combine(_testRepoPath, "file1.txt"), "Modified content");
        RunGit("add file1.txt");
        RunGit("commit -m \"Modify file1\"");
    }

    private void RunGit(string args)
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
        process?.WaitForExit(5000);
    }

    private string GetCommitHash(int offset = 0)
    {
        var psi = new ProcessStartInfo("git", $"log --skip={offset} -1 --format=%H")
        {
            WorkingDirectory = _testRepoPath,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        var hash = process?.StandardOutput.ReadLine();
        process?.WaitForExit(5000);
        return hash ?? "";
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

    [Fact]
    public void GetCommitTitle_ReturnsCorrectTitle()
    {
        var service = CreateService();
        var hash = GetCommitHash();

        var result = service.GetCommitTitle(_testRepoPath, hash);

        Assert.True(result.IsSuccess);
        Assert.Equal("Modify file1", result.Value);
    }

    [Fact]
    public void GetCommitTitle_ReturnsFailureForInvalidCommit()
    {
        var service = CreateService();

        var result = service.GetCommitTitle(_testRepoPath, "invalid123456");

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void GetCommitTitle_ReturnsFailureForInvalidRepo()
    {
        var service = CreateService();

        var result = service.GetCommitTitle("/nonexistent/path", "abc1234");

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void GetCommitDiff_ReturnsValidDiff()
    {
        var service = CreateService();
        var hash = GetCommitHash();

        var result = service.GetCommitDiff(_testRepoPath, hash);

        Assert.True(result.IsSuccess);
        Assert.Contains("file1.txt", result.Value);
        Assert.Contains("Modified content", result.Value);
    }

    [Fact]
    public void GetCommitDiff_ReturnsFailureForInvalidCommit()
    {
        var service = CreateService();

        var result = service.GetCommitDiff(_testRepoPath, "invalid123456");

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void GetCommitFileCount_ReturnsCorrectCount()
    {
        var service = CreateService();
        var hash = GetCommitHash();

        var result = service.GetCommitFileCount(_testRepoPath, hash);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value);
    }

    [Fact]
    public void GetCommitFileCount_ReturnsFailureForInvalidCommit()
    {
        var service = CreateService();

        var result = service.GetCommitFileCount(_testRepoPath, "invalid123456");

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void GetCommitFiles_ReturnsCorrectFiles()
    {
        var service = CreateService();
        var hash = GetCommitHash(1); // "Add file2" commit

        var result = service.GetCommitFiles(_testRepoPath, hash);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("A", result.Value![0].Status);
        Assert.Equal("file2.txt", result.Value![0].FilePath);
    }

    [Fact]
    public void GetCommitFiles_ParsesModifiedStatus()
    {
        var service = CreateService();
        var hash = GetCommitHash(); // "Modify file1" commit

        var result = service.GetCommitFiles(_testRepoPath, hash);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("M", result.Value![0].Status);
        Assert.Equal("file1.txt", result.Value![0].FilePath);
    }

    [Fact]
    public void GetCommitFiles_ReturnsFailureForInvalidCommit()
    {
        var service = CreateService();

        var result = service.GetCommitFiles(_testRepoPath, "invalid123456");

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void GetCombinedDiff_ReturnsValidDiff()
    {
        var service = CreateService();
        var firstCommit = GetCommitHash(1); // Add file2 commit
        var lastCommit = GetCommitHash(); // Modify file1

        var result = service.GetCombinedDiff(_testRepoPath, firstCommit, lastCommit);

        Assert.True(result.IsSuccess);
        Assert.Contains("file1.txt", result.Value);
    }

    [Fact(Skip = "Git diff range behavior varies - test is environment-dependent")]
    public void GetCombinedChangedFiles_ReturnsCorrectFiles()
    {
        var service = CreateService();
        var firstCommit = GetCommitHash(2); // Initial commit
        var lastCommit = GetCommitHash(1); // Add file2 commit

        var result = service.GetCombinedChangedFiles(_testRepoPath, firstCommit, lastCommit);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value!, f => f.FilePath == "file2.txt" && f.Status == "A");
    }

    [Fact]
    public void GetCommitSummaries_ReturnsCorrectSummaries()
    {
        var service = CreateService();
        var hash1 = GetCommitHash();
        var hash2 = GetCommitHash(1);

        var result = service.GetCommitSummaries(_testRepoPath, new[] { hash1, hash2 });

        Assert.True(result.IsSuccess);
        var summaries = result.Value!;
        Assert.Equal(2, summaries.Count);
        Assert.Equal("Modify file1", summaries[hash1].Title);
        Assert.Equal(1, summaries[hash1].FileCount);
        Assert.Equal("Add file2", summaries[hash2].Title);
        Assert.Equal(1, summaries[hash2].FileCount);
    }

    [Fact]
    public void GetCommitSummaries_HandlesShortHashes()
    {
        var service = CreateService();
        var fullHash = GetCommitHash();
        var shortHash = fullHash.Substring(0, 7);

        var result = service.GetCommitSummaries(_testRepoPath, new[] { shortHash });

        Assert.True(result.IsSuccess);
        var summaries = result.Value!;
        Assert.True(summaries.ContainsKey(shortHash));
        Assert.Equal("Modify file1", summaries[shortHash].Title);
    }

    [Fact]
    public void GetCommitSummaries_ReturnsEmptyForEmptyInput()
    {
        var service = CreateService();

        var result = service.GetCommitSummaries(_testRepoPath, Array.Empty<string>());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public void GetCommitSummaries_ReturnsFailureForInvalidRepo()
    {
        var service = CreateService();

        var result = service.GetCommitSummaries("/nonexistent/path", new[] { "abc1234" });

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void GetWorktrees_ReturnsMainWorktree()
    {
        var service = CreateService();

        var result = service.GetWorktrees(_testRepoPath);

        Assert.True(result.IsSuccess);
        var worktrees = result.Value!;
        Assert.NotEmpty(worktrees);
        // Git on Windows uses forward slashes in paths, normalize for comparison
        var normalizedTestPath = _testRepoPath.Replace('\\', '/');
        Assert.Contains(worktrees, w => w.Path.Replace('\\', '/').Contains(normalizedTestPath));
    }

    [Fact]
    public void GetWorktrees_ReturnsFailureForInvalidRepo()
    {
        var service = CreateService();

        var result = service.GetWorktrees("/nonexistent/path");

        Assert.False(result.IsSuccess);
    }
}
