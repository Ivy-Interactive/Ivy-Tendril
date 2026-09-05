using System.Diagnostics;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Git;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test.Services;

public class GitServiceBranchesTests : IDisposable
{
    private readonly string _testRepoPath;
    private readonly GitService _gitService;

    public GitServiceBranchesTests()
    {
        _testRepoPath = Path.Combine(Path.GetTempPath(), $"git-branches-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testRepoPath);
        var configService = new ConfigService(new TendrilSettings());
        _gitService = new GitService(configService, NullLogger<GitService>.Instance);
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
        using var process = Process.Start(psi)!;
        process.WaitForExit();
    }

    [Fact]
    public void GetBranches_ParsesAndDeduplicatesRefs()
    {
        RunGit("init");
        RunGit("config user.email test@example.com");
        RunGit("config user.name TestUser");

        File.WriteAllText(Path.Combine(_testRepoPath, "file.txt"), "hello");
        RunGit("add file.txt");
        RunGit("commit -m \"Initial commit\"");
        RunGit("branch -M main");

        // Create branches
        RunGit("branch feature/branch-b");
        RunGit("branch feature/branch-a");

        // Simulate remote refs
        RunGit("update-ref refs/remotes/origin/main HEAD");
        RunGit("update-ref refs/remotes/origin/feature/branch-a HEAD");
        RunGit("update-ref refs/remotes/origin/feature/remote-only HEAD");
        RunGit("update-ref refs/remotes/origin/HEAD HEAD");

        var result = _gitService.GetBranches(_testRepoPath);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);

        // HEAD and origin should be excluded
        Assert.DoesNotContain("HEAD", result.Value, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("origin", result.Value, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("origin/HEAD", result.Value, StringComparer.OrdinalIgnoreCase);

        // Remote prefix should be stripped
        Assert.DoesNotContain("origin/main", result.Value, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("origin/feature/branch-a", result.Value, StringComparer.OrdinalIgnoreCase);

        // Deduplication: feature/branch-a exists in heads and remotes/origin, should only appear once
        Assert.Single(result.Value, b => string.Equals(b, "feature/branch-a", StringComparison.OrdinalIgnoreCase));

        // Expected sorted branches
        var expected = new[] { "feature/branch-a", "feature/branch-b", "feature/remote-only", "main" };
        Assert.Equal(expected, result.Value);
    }
}
