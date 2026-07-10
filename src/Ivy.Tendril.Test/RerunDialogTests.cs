using Ivy.Tendril.Services.Git;

namespace Ivy.Tendril.Test;

public class ResetToDraftDialogTests
{
    [Fact]
    public void CleanPlanState_DeletesArtifactsAndLegacyPlanLogs()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ivy-test-{Guid.NewGuid()}");
        try
        {
            var planDir = Path.Combine(tempDir, "00001-TestPlan");
            var artifactsDir = Path.Combine(planDir, "Artifacts");
            // Nothing writes plan Logs/ any more (job logs live in <TendrilHome>/Jobs/), but a plan
            // created before the move still carries one and a reset must sweep it away.
            var legacyLogsDir = Path.Combine(planDir, "Logs");
            Directory.CreateDirectory(artifactsDir);
            Directory.CreateDirectory(legacyLogsDir);
            File.WriteAllText(Path.Combine(artifactsDir, "summary.md"), "test");
            File.WriteAllText(Path.Combine(legacyLogsDir, "001.md"), "test");

            WorktreeCleanupService.CleanPlanState(planDir);

            Assert.False(Directory.Exists(artifactsDir));
            Assert.False(Directory.Exists(legacyLogsDir));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CleanPlanState_DoesNotTouchJobLogs()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ivy-test-{Guid.NewGuid()}");
        try
        {
            var planDir = Path.Combine(tempDir, "Plans", "00001-TestPlan");
            Directory.CreateDirectory(planDir);
            var jobsDir = Path.Combine(tempDir, "Jobs");
            Directory.CreateDirectory(jobsDir);
            var jobLog = Path.Combine(jobsDir, "00005-00001-ExecutePlan.md");
            File.WriteAllText(jobLog, "# Job Log");

            WorktreeCleanupService.CleanPlanState(planDir);

            // Resetting a plan must not erase the forensic record of the runs that happened.
            Assert.True(File.Exists(jobLog));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CleanPlanState_HandlesNonExistentDirectories()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ivy-test-{Guid.NewGuid()}");
        try
        {
            var planDir = Path.Combine(tempDir, "00001-TestPlan");
            Directory.CreateDirectory(planDir);

            var ex = Record.Exception(() => WorktreeCleanupService.CleanPlanState(planDir));
            Assert.Null(ex);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CleanPlanState_PreservesOtherDirectories()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ivy-test-{Guid.NewGuid()}");
        try
        {
            var planDir = Path.Combine(tempDir, "00001-TestPlan");
            var artifactsDir = Path.Combine(planDir, "Artifacts");
            var legacyLogsDir = Path.Combine(planDir, "Logs");
            var verificationDir = Path.Combine(planDir, "Verification");
            var revisionsDir = Path.Combine(planDir, "Revisions");
            Directory.CreateDirectory(artifactsDir);
            Directory.CreateDirectory(legacyLogsDir);
            Directory.CreateDirectory(verificationDir);
            Directory.CreateDirectory(revisionsDir);

            WorktreeCleanupService.CleanPlanState(planDir);

            Assert.False(Directory.Exists(artifactsDir));
            Assert.False(Directory.Exists(legacyLogsDir));
            Assert.False(Directory.Exists(verificationDir));
            Assert.True(Directory.Exists(revisionsDir));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CleanPlanState_DeletesNestedArtifacts()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ivy-test-{Guid.NewGuid()}");
        try
        {
            var planDir = Path.Combine(tempDir, "00001-TestPlan");
            var screenshotsDir = Path.Combine(planDir, "Artifacts", "screenshots");
            var sampleDir = Path.Combine(planDir, "Artifacts", "sample", "bin");
            Directory.CreateDirectory(screenshotsDir);
            Directory.CreateDirectory(sampleDir);
            File.WriteAllText(Path.Combine(screenshotsDir, "img.png"), "test");
            File.WriteAllText(Path.Combine(sampleDir, "app.dll"), "test");

            WorktreeCleanupService.CleanPlanState(planDir);

            Assert.False(Directory.Exists(Path.Combine(planDir, "Artifacts")));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CleanPlanState_DeletesWorktreesDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ivy-test-{Guid.NewGuid()}");
        try
        {
            var planDir = Path.Combine(tempDir, "00001-TestPlan");
            var worktreesDir = Path.Combine(planDir, "Worktrees");
            var repoDir = Path.Combine(worktreesDir, "Ivy-Framework");
            Directory.CreateDirectory(repoDir);
            File.WriteAllText(Path.Combine(repoDir, "dummy.txt"), "test");

            WorktreeCleanupService.CleanPlanState(planDir);

            Assert.False(Directory.Exists(worktreesDir));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
