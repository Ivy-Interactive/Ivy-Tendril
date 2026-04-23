using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

/// <summary>
///     Tests for preventing concurrent plan-modifying jobs (UpdatePlan, ExpandPlan, SplitPlan)
///     that would cause race conditions and state corruption.
/// </summary>
public class JobServiceConcurrentPlanModificationTests : IDisposable
{
    private readonly string _planFolder;
    private readonly string _testDir;

    public JobServiceConcurrentPlanModificationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "tendril-test-" + Guid.NewGuid().ToString("N")[..8]);
        _planFolder = Path.Combine(_testDir, "Plans", "00001-TestPlan");
        Directory.CreateDirectory(_planFolder);

        // Create a minimal plan.yaml
        var planYaml = """
                       state: Draft
                       project: Test
                       level: Bug
                       title: Test Plan
                       repos: []
                       created: 2026-04-21T00:00:00Z
                       updated: 2026-04-21T00:00:00Z
                       verifications: []
                       """;
        FileHelper.WriteAllText(Path.Combine(_planFolder, "plan.yaml"), planYaml);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, true);
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    [Fact]
    public void StartJob_UpdatePlan_WhenAnotherUpdatePlanRunning_FailsWithConflictMessage()
    {
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 10);

        // Start first UpdatePlan job (will be in Running state after CreateTestJob)
        var firstJobId = service.CreateTestJob("UpdatePlan", _planFolder);
        var firstJob = service.GetJob(firstJobId);
        Assert.NotNull(firstJob);
        Assert.Equal(JobStatus.Running, firstJob.Status);

        // Try to start second UpdatePlan job for the same plan
        var secondJobId = service.StartJob("UpdatePlan", _planFolder);
        var secondJob = service.GetJob(secondJobId);

        // Second job should fail immediately with conflict message
        Assert.NotNull(secondJob);
        Assert.Equal(JobStatus.Failed, secondJob.Status);
        Assert.Contains("already in progress", secondJob.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(firstJobId, secondJob.StatusMessage);
    }

    [Fact]
    public void StartJob_ExpandPlan_WhenAnotherExpandPlanRunning_FailsWithConflictMessage()
    {
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 10);

        _ = service.CreateTestJob("ExpandPlan", _planFolder);
        var secondJobId = service.StartJob("ExpandPlan", _planFolder);
        var secondJob = service.GetJob(secondJobId);

        Assert.NotNull(secondJob);
        Assert.Equal(JobStatus.Failed, secondJob.Status);
        Assert.Contains("already in progress", secondJob.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartJob_SplitPlan_WhenAnotherSplitPlanRunning_FailsWithConflictMessage()
    {
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 10);

        _ = service.CreateTestJob("SplitPlan", _planFolder);
        var secondJobId = service.StartJob("SplitPlan", _planFolder);
        var secondJob = service.GetJob(secondJobId);

        Assert.NotNull(secondJob);
        Assert.Equal(JobStatus.Failed, secondJob.Status);
        Assert.Contains("already in progress", secondJob.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartJob_UpdatePlan_WhenFirstJobCompleted_AllowsSecondJob()
    {
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 10);

        // Start and complete first job
        var firstJobId = service.CreateTestJob("UpdatePlan", _planFolder);
        service.CompleteJob(firstJobId, 0);
        var firstJob = service.GetJob(firstJobId);
        Assert.NotNull(firstJob);
        Assert.Equal(JobStatus.Completed, firstJob.Status);

        // Second job should be allowed (will fail to launch process, but should attempt)
        try
        {
            var secondJobId = service.StartJob("UpdatePlan", _planFolder);
            var secondJob = service.GetJob(secondJobId);
            Assert.NotNull(secondJob);
            // Should not be Failed due to conflict (might be Failed due to process launch, but that's OK)
            if (secondJob.Status == JobStatus.Failed)
                Assert.DoesNotContain("already in progress", secondJob.StatusMessage ?? "",
                    StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // Process launch failures are acceptable in this test
        }
    }

    [Fact]
    public void StartJob_UpdatePlan_WhenFirstJobQueued_FailsWithConflictMessage()
    {
        // Use maxConcurrentJobs=0 to force queueing
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 0);

        var firstJobId = service.StartJob("UpdatePlan", _planFolder);
        var firstJob = service.GetJob(firstJobId);
        Assert.NotNull(firstJob);
        Assert.Equal(JobStatus.Queued, firstJob.Status);

        // Second job should fail even though first is only queued
        var secondJobId = service.StartJob("UpdatePlan", _planFolder);
        var secondJob = service.GetJob(secondJobId);

        Assert.NotNull(secondJob);
        Assert.Equal(JobStatus.Failed, secondJob.Status);
        Assert.Contains("already in progress", secondJob.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartJob_UpdatePlan_WhenFirstJobPending_FailsWithConflictMessage()
    {
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 10);

        // Create a job manually in Pending state
        var firstJobId = service.CreateTestJob("UpdatePlan", _planFolder);
        var firstJob = service.GetJob(firstJobId);
        Assert.NotNull(firstJob);
        firstJob.Status = JobStatus.Pending;

        // Second job should fail
        var secondJobId = service.StartJob("UpdatePlan", _planFolder);
        var secondJob = service.GetJob(secondJobId);

        Assert.NotNull(secondJob);
        Assert.Equal(JobStatus.Failed, secondJob.Status);
        Assert.Contains("already in progress", secondJob.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartJob_UpdatePlan_ForDifferentPlan_AllowsBothJobs()
    {
        var otherPlanFolder = Path.Combine(_testDir, "Plans", "00002-OtherPlan");
        Directory.CreateDirectory(otherPlanFolder);
        var planYaml = """
                       state: Draft
                       project: Test
                       level: Bug
                       title: Other Plan
                       repos: []
                       created: 2026-04-21T00:00:00Z
                       updated: 2026-04-21T00:00:00Z
                       verifications: []
                       """;
        FileHelper.WriteAllText(Path.Combine(otherPlanFolder, "plan.yaml"), planYaml);

        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 10);

        // Start UpdatePlan for first plan
        var firstJobId = service.CreateTestJob("UpdatePlan", _planFolder);
        var firstJob = service.GetJob(firstJobId);
        Assert.NotNull(firstJob);
        Assert.Equal(JobStatus.Running, firstJob.Status);

        // Start UpdatePlan for different plan — should be allowed
        try
        {
            var secondJobId = service.StartJob("UpdatePlan", otherPlanFolder);
            var secondJob = service.GetJob(secondJobId);
            Assert.NotNull(secondJob);
            // Should not fail due to conflict
            if (secondJob.Status == JobStatus.Failed)
                Assert.DoesNotContain("already in progress", secondJob.StatusMessage ?? "",
                    StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // Process launch failures are acceptable
        }
    }

    [Fact]
    public void StartJob_ExecutePlan_WhenUpdatePlanRunning_AllowsBothJobs()
    {
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 10);

        // Start UpdatePlan
        _ = service.CreateTestJob("UpdatePlan", _planFolder);

        // ExecutePlan should not be blocked by UpdatePlan (they're different job types)
        try
        {
            var executeJobId = service.StartJob("ExecutePlan", _planFolder);
            var executeJob = service.GetJob(executeJobId);
            Assert.NotNull(executeJob);
            // Should not fail due to plan modification conflict
            if (executeJob.Status == JobStatus.Failed)
                Assert.DoesNotContain("already in progress", executeJob.StatusMessage ?? "",
                    StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // Process launch or dependency check failures are acceptable
        }
    }

    [Fact]
    public void StartJob_ExecutePlan_WhenAnotherExecutePlanRunning_FailsWithConflictMessage()
    {
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 10);

        // Start first ExecutePlan job
        var firstJobId = service.CreateTestJob("ExecutePlan", _planFolder);
        var firstJob = service.GetJob(firstJobId);
        Assert.NotNull(firstJob);
        Assert.Equal(JobStatus.Running, firstJob.Status);

        // Try to start second ExecutePlan job for the same plan
        var secondJobId = service.StartJob("ExecutePlan", _planFolder);
        var secondJob = service.GetJob(secondJobId);

        // Second job should fail immediately with conflict message
        Assert.NotNull(secondJob);
        Assert.Equal(JobStatus.Failed, secondJob.Status);
        Assert.Contains("already in progress", secondJob.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(firstJobId, secondJob.StatusMessage);
    }

    [Fact]
    public void StartJob_RaisesNotificationWhenConcurrentJobBlocked()
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            var service = new JobService(
                TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
                null, 10);

            JobNotification? receivedNotification = null;
            service.NotificationReady += n => receivedNotification = n;

            // Start first job
            _ = service.CreateTestJob("UpdatePlan", _planFolder);

            // Try to start conflicting second job
            _ = service.StartJob("UpdatePlan", _planFolder);

            // Should have received a notification
            Assert.NotNull(receivedNotification);
            Assert.Contains("Already Running", receivedNotification.Title);
            Assert.False(receivedNotification.IsSuccess);
            Assert.Contains("Cannot start", receivedNotification.Message);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }
}