using System.Diagnostics;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

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

    [Fact]
    public void GetCommitTitle_ReturnsCorrectTitle()
    {
        var service = new GitService(_configService);
        var hash = GetCommitHash();

        var title = service.GetCommitTitle(_testRepoPath, hash);

        Assert.Equal("Modify file1", title);
    }

    [Fact]
    public void GetCommitTitle_ReturnsNullForInvalidCommit()
    {
        var service = new GitService(_configService);

        var title = service.GetCommitTitle(_testRepoPath, "invalid123456");

        Assert.Null(title);
    }

    [Fact]
    public void GetCommitTitle_ReturnsNullForInvalidRepo()
    {
        var service = new GitService(_configService);

        var title = service.GetCommitTitle("/nonexistent/path", "abc1234");

        Assert.Null(title);
    }

    [Fact]
    public void GetCommitDiff_ReturnsValidDiff()
    {
        var service = new GitService(_configService);
        var hash = GetCommitHash();

        var diff = service.GetCommitDiff(_testRepoPath, hash);

        Assert.NotNull(diff);
        Assert.Contains("file1.txt", diff);
        Assert.Contains("Modified content", diff);
    }

    [Fact]
    public void GetCommitDiff_ReturnsNullForInvalidCommit()
    {
        var service = new GitService(_configService);

        var diff = service.GetCommitDiff(_testRepoPath, "invalid123456");

        Assert.Null(diff);
    }

    [Fact]
    public void GetCommitFileCount_ReturnsCorrectCount()
    {
        var service = new GitService(_configService);
        var hash = GetCommitHash();

        var count = service.GetCommitFileCount(_testRepoPath, hash);

        Assert.Equal(1, count);
    }

    [Fact]
    public void GetCommitFileCount_ReturnsNullForInvalidCommit()
    {
        var service = new GitService(_configService);

        var count = service.GetCommitFileCount(_testRepoPath, "invalid123456");

        Assert.Null(count);
    }

    [Fact]
    public void GetCommitFiles_ReturnsCorrectFiles()
    {
        var service = new GitService(_configService);
        var hash = GetCommitHash(1); // "Add file2" commit

        var files = service.GetCommitFiles(_testRepoPath, hash);

        Assert.NotNull(files);
        Assert.Single(files);
        Assert.Equal("A", files[0].Status);
        Assert.Equal("file2.txt", files[0].FilePath);
    }

    [Fact]
    public void GetCommitFiles_ParsesModifiedStatus()
    {
        var service = new GitService(_configService);
        var hash = GetCommitHash(); // "Modify file1" commit

        var files = service.GetCommitFiles(_testRepoPath, hash);

        Assert.NotNull(files);
        Assert.Single(files);
        Assert.Equal("M", files[0].Status);
        Assert.Equal("file1.txt", files[0].FilePath);
    }

    [Fact]
    public void GetCommitFiles_ReturnsNullForInvalidCommit()
    {
        var service = new GitService(_configService);

        var files = service.GetCommitFiles(_testRepoPath, "invalid123456");

        Assert.Null(files);
    }

    [Fact]
    public void GetCombinedDiff_ReturnsValidDiff()
    {
        var service = new GitService(_configService);
        var firstCommit = GetCommitHash(1); // Add file2 commit
        var lastCommit = GetCommitHash(); // Modify file1

        var diff = service.GetCombinedDiff(_testRepoPath, firstCommit, lastCommit);

        Assert.NotNull(diff);
        Assert.Contains("file1.txt", diff);
    }

    [Fact]
    public void GetCombinedChangedFiles_ReturnsCorrectFiles()
    {
        var service = new GitService(_configService);
        var firstCommit = GetCommitHash(2); // Initial commit
        var lastCommit = GetCommitHash(1); // Add file2 commit

        var files = service.GetCombinedChangedFiles(_testRepoPath, firstCommit, lastCommit);

        Assert.NotNull(files);
        Assert.Single(files);
        Assert.Contains(files, f => f.FilePath == "file2.txt" && f.Status == "A");
    }

    [Fact]
    public void GetCommitSummaries_ReturnsCorrectSummaries()
    {
        var service = new GitService(_configService);
        var hash1 = GetCommitHash();
        var hash2 = GetCommitHash(1);

        var summaries = service.GetCommitSummaries(_testRepoPath, new[] { hash1, hash2 });

        Assert.NotNull(summaries);
        Assert.Equal(2, summaries.Count);
        Assert.Equal("Modify file1", summaries[hash1].Title);
        Assert.Equal(1, summaries[hash1].FileCount);
        Assert.Equal("Add file2", summaries[hash2].Title);
        Assert.Equal(1, summaries[hash2].FileCount);
    }

    [Fact]
    public void GetCommitSummaries_HandlesShortHashes()
    {
        var service = new GitService(_configService);
        var fullHash = GetCommitHash();
        var shortHash = fullHash.Substring(0, 7);

        var summaries = service.GetCommitSummaries(_testRepoPath, new[] { shortHash });

        Assert.NotNull(summaries);
        Assert.True(summaries.ContainsKey(shortHash));
        Assert.Equal("Modify file1", summaries[shortHash].Title);
    }

    [Fact]
    public void GetCommitSummaries_ReturnsEmptyForEmptyInput()
    {
        var service = new GitService(_configService);

        var summaries = service.GetCommitSummaries(_testRepoPath, Array.Empty<string>());

        Assert.NotNull(summaries);
        Assert.Empty(summaries);
    }

    [Fact]
    public void GetCommitSummaries_ReturnsNullForInvalidRepo()
    {
        var service = new GitService(_configService);

        var summaries = service.GetCommitSummaries("/nonexistent/path", new[] { "abc1234" });

        Assert.Null(summaries);
    }

    [Fact]
    public void GetWorktrees_ReturnsMainWorktree()
    {
        var service = new GitService(_configService);

        var worktrees = service.GetWorktrees(_testRepoPath);

        Assert.NotNull(worktrees);
        Assert.NotEmpty(worktrees);
        // Git on Windows uses forward slashes in paths, normalize for comparison
        var normalizedTestPath = _testRepoPath.Replace('\\', '/');
        Assert.Contains(worktrees, w => w.Path.Replace('\\', '/').Contains(normalizedTestPath));
    }

    [Fact]
    public void GetWorktrees_ReturnsNullForInvalidRepo()
    {
        var service = new GitService(_configService);

        var worktrees = service.GetWorktrees("/nonexistent/path");

        Assert.Null(worktrees);
    }
}
