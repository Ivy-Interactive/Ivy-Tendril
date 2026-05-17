using Ivy.Tendril.Apps.Review.Dialogs;

namespace Ivy.Tendril.Test;

public class ResetToDraftDialogTests
{
    [Fact]
    public void CleanPlanState_DeletesArtifactsAndLogs()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ivy-test-{Guid.NewGuid()}");
        try
        {
            var planDir = Path.Combine(tempDir, "00001-TestPlan");
            var artifactsDir = Path.Combine(planDir, "Artifacts");
            var logsDir = Path.Combine(planDir, "Logs");
            Directory.CreateDirectory(artifactsDir);
            Directory.CreateDirectory(logsDir);
            File.WriteAllText(Path.Combine(artifactsDir, "summary.md"), "test");
            File.WriteAllText(Path.Combine(logsDir, "001.md"), "test");

            ResetToDraftDialog.CleanPlanState(planDir);

            Assert.False(Directory.Exists(artifactsDir));
            Assert.False(Directory.Exists(logsDir));
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

            var ex = Record.Exception(() => ResetToDraftDialog.CleanPlanState(planDir));
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
            var logsDir = Path.Combine(planDir, "Logs");
            var verificationDir = Path.Combine(planDir, "Verification");
            var revisionsDir = Path.Combine(planDir, "Revisions");
            Directory.CreateDirectory(artifactsDir);
            Directory.CreateDirectory(logsDir);
            Directory.CreateDirectory(verificationDir);
            Directory.CreateDirectory(revisionsDir);

            ResetToDraftDialog.CleanPlanState(planDir);

            Assert.False(Directory.Exists(artifactsDir));
            Assert.False(Directory.Exists(logsDir));
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

            ResetToDraftDialog.CleanPlanState(planDir);

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

            ResetToDraftDialog.CleanPlanState(planDir);

            Assert.False(Directory.Exists(worktreesDir));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}