using System.Reflection;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

public class ExecutePlanWorktreeCleanupTests : IDisposable
{
    private readonly string _tempDir;

    public ExecutePlanWorktreeCleanupTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tendril-ep-cleanup-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private string CreateFakePlan(string name)
    {
        var planDir = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(planDir);
        File.WriteAllText(Path.Combine(planDir, "plan.yaml"),
            "state: Completed\nproject: Test\ntitle: Test Plan\nupdated: 2020-01-01T00:00:00Z\n");
        return planDir;
    }

    private string CreateWorktreeDir(string planDir, string repoName, bool withGitFile = false)
    {
        var wtDir = Path.Combine(planDir, "Worktrees", repoName);
        Directory.CreateDirectory(wtDir);
        File.WriteAllText(Path.Combine(wtDir, "dummy.cs"), "// test");

        if (withGitFile)
            File.WriteAllText(Path.Combine(wtDir, ".git"),
                "gitdir: /nonexistent/.git/worktrees/" + repoName);

        return wtDir;
    }

    [Fact]
    public void RemoveWorktrees_Removes_Worktree_Directory()
    {
        var planDir = CreateFakePlan("01000-CleanupTest");
        var wtDir = CreateWorktreeDir(planDir, "TestRepo");

        Assert.True(Directory.Exists(wtDir));

        WorktreeCleanupService.RemoveWorktrees(planDir);

        Assert.False(Directory.Exists(wtDir), "Worktree directory should be removed");
    }

    [Fact]
    public void RemoveWorktrees_Handles_Orphaned_Worktree_Without_GitFile()
    {
        var planDir = CreateFakePlan("02000-OrphanTest");
        var wtDir = CreateWorktreeDir(planDir, "OrphanRepo");

        WorktreeCleanupService.RemoveWorktrees(planDir);

        Assert.False(Directory.Exists(wtDir), "Orphaned worktree without .git should still be cleaned");
    }

    [Fact]
    public void CleanupPlanWorktrees_Handles_Stale_GitFile()
    {
        var planDir = CreateFakePlan("03000-StaleGitTest");
        var wtDir = CreateWorktreeDir(planDir, "StaleRepo", true);

        // CleanupPlanWorktrees includes fallback force-delete for directories remaining after RemoveWorktrees
        WorktreeCleanupService.CleanupPlanWorktrees(planDir);

        Assert.False(Directory.Exists(wtDir), "Worktree with stale .git should be cleaned");
    }

    [Fact]
    public void RemoveWorktrees_NoOp_When_No_Worktrees_Directory()
    {
        var planDir = CreateFakePlan("04000-NoWorktreeDir");

        WorktreeCleanupService.RemoveWorktrees(planDir);

        Assert.True(Directory.Exists(planDir));
    }

    [Fact]
    public void RemoveWorktrees_Removes_Multiple_Worktrees()
    {
        var planDir = CreateFakePlan("05000-MultiWorktree");
        CreateWorktreeDir(planDir, "RepoA");
        CreateWorktreeDir(planDir, "RepoB");
        CreateWorktreeDir(planDir, "RepoC");

        WorktreeCleanupService.RemoveWorktrees(planDir);

        Assert.False(Directory.Exists(Path.Combine(planDir, "Worktrees", "RepoA")));
        Assert.False(Directory.Exists(Path.Combine(planDir, "Worktrees", "RepoB")));
        Assert.False(Directory.Exists(Path.Combine(planDir, "Worktrees", "RepoC")));
    }

    [Fact]
    public void GracePeriod_Is_Ten_Minutes()
    {
        var field = typeof(WorktreeCleanupService)
            .GetField("GracePeriod", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var value = (TimeSpan)field!.GetValue(null)!;
        Assert.Equal(TimeSpan.FromMinutes(10), value);
    }
}
