using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services.Plans;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test;

public class PlanArtifactSyncerTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "PlanArtifactSyncerTests_" + Guid.NewGuid().ToString("N")[..8]);

    public PlanArtifactSyncerTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    private static void RunGitFor(string args, string workingDir)
    {
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", args)
        {
            WorkingDirectory = workingDir,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        });
        p?.WaitForExit(15000);
    }

    private string? SetUpRepoWithWorktree(string planFolder, string worktreeFolderName, string branchName, string baseBranch = "main")
    {
        var remote = Path.Combine(_tempDir, $"remote-{Guid.NewGuid():N}.git");
        Directory.CreateDirectory(remote);
        RunGitFor($"init --bare -b {baseBranch}", remote);

        var repo = Path.Combine(_tempDir, $"repo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repo);
        RunGitFor($"init -b {baseBranch}", repo);
        RunGitFor("config user.email \"test@example.com\"", repo);
        RunGitFor("config user.name \"Test User\"", repo);
        File.WriteAllText(Path.Combine(repo, "a.txt"), "base");
        RunGitFor("add -A", repo);
        RunGitFor("commit -m initial", repo);
        RunGitFor($"remote add origin \"{remote}\"", repo);
        RunGitFor($"push -u origin {baseBranch}", repo);
        RunGitFor($"remote set-head origin {baseBranch}", repo);

        if (!Directory.Exists(Path.Combine(repo, ".git")))
            return null;

        var worktreesDir = Path.Combine(planFolder, "Worktrees");
        Directory.CreateDirectory(worktreesDir);
        var worktreePath = Path.Combine(worktreesDir, worktreeFolderName);
        RunGitFor($"worktree add -b {branchName} \"{worktreePath}\" {baseBranch}", repo);

        if (!File.Exists(Path.Combine(worktreePath, ".git")))
            return null;

        File.WriteAllText(Path.Combine(worktreePath, "b.txt"), "change");
        RunGitFor("add -A", worktreePath);
        RunGitFor("commit -m \"agent change\"", worktreePath);

        return repo;
    }

    private string CreatePlanFolder(string project, string configuredRepoPath)
    {
        var planFolder = Path.Combine(_tempDir, "00001-TestPlan");
        Directory.CreateDirectory(planFolder);
        Directory.CreateDirectory(configuredRepoPath);
        File.WriteAllText(Path.Combine(planFolder, "plan.yaml"),
            $"state: Review\nproject: {project}\ntitle: Test Plan\nrepos:\n  - {configuredRepoPath}\nupdated: {DateTime.UtcNow:o}\n");
        return planFolder;
    }

    [Fact]
    public void SyncPlanArtifacts_WorktreeFolderNameMismatchesPlanRepos_StillSyncsCommits()
    {
        var configuredRepo = Path.Combine(_tempDir, "configured-repo-name");
        var planFolder = CreatePlanFolder("Test", configuredRepo);
        var branchName = "tendril/00001-TestPlan";
        var repo = SetUpRepoWithWorktree(planFolder, "actual-worktree-folder", branchName);
        if (repo == null) return;

        var syncer = new PlanArtifactSyncer(configService: null, NullLogger.Instance, planWatcherService: null);
        syncer.SyncPlanArtifacts(planFolder);

        var plan = PlanCommandHelpers.ReadPlan(planFolder);
        Assert.Single(plan.Commits);
    }

    [Fact]
    public void SyncPlanArtifacts_WorktreeFolderNameMatchesPlanRepos_SyncsCommits()
    {
        var configuredRepo = Path.Combine(_tempDir, "matching-repo");
        var planFolder = CreatePlanFolder("Test", configuredRepo);
        var branchName = "tendril/00001-TestPlan";
        var repo = SetUpRepoWithWorktree(planFolder, "matching-repo", branchName);
        if (repo == null) return;

        var syncer = new PlanArtifactSyncer(configService: null, NullLogger.Instance, planWatcherService: null);
        syncer.SyncPlanArtifacts(planFolder);

        var plan = PlanCommandHelpers.ReadPlan(planFolder);
        Assert.Single(plan.Commits);
    }
}
