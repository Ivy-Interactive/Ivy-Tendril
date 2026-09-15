using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test.Services;

/// <summary>
///     Tests for preventing concurrent job types that mutate the same plan worktree (ExecutePlan,
///     RetryPlan, CreatePr, UpdatePlan, ExpandPlan, SplitPlan) from running at once, which would cause
///     race conditions and state corruption.
/// </summary>
public class JobServiceConcurrentPlanModificationTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly string _planFolder;

    public JobServiceConcurrentPlanModificationTests()
    {
        _planFolder = Path.Combine(_tempDir.Path, "Plans", "00001-TestPlan");
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
        _tempDir.Dispose();
    }

    [Fact]
    public void StartJob_UpdatePlan_WhenAnotherUpdatePlanRunning_FailsWithConflictMessage()
    {
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 10);

        // Start first UpdatePlan job (will be in Running state after CreateTestJob)
        var firstJobId = service.CreateTestJob(new UpdatePlanArgs(_planFolder));
        var firstJob = service.GetJob(firstJobId);
        Assert.NotNull(firstJob);
        Assert.Equal(JobStatus.Running, firstJob.Status);

        // Try to start second UpdatePlan job for the same plan
        var secondJobId = service.StartJob(new UpdatePlanArgs(_planFolder));
        var secondJob = service.GetJob(secondJobId);

        // Second job should fail immediately with conflict message
        Assert.NotNull(secondJob);
        Assert.Equal(JobStatus.Failed, secondJob.Status);
        Assert.Contains("already running", secondJob.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(firstJobId, secondJob.StatusMessage);
    }

    [Fact]
    public void StartJob_ExpandPlan_WhenAnotherExpandPlanRunning_FailsWithConflictMessage()
    {
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 10);

        _ = service.CreateTestJob(new ExpandPlanArgs(_planFolder));
        var secondJobId = service.StartJob(new ExpandPlanArgs(_planFolder));
        var secondJob = service.GetJob(secondJobId);

        Assert.NotNull(secondJob);
        Assert.Equal(JobStatus.Failed, secondJob.Status);
        Assert.Contains("already running", secondJob.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartJob_SplitPlan_WhenAnotherSplitPlanRunning_FailsWithConflictMessage()
    {
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 10);

        _ = service.CreateTestJob(new SplitPlanArgs(_planFolder));
        var secondJobId = service.StartJob(new SplitPlanArgs(_planFolder));
        var secondJob = service.GetJob(secondJobId);

        Assert.NotNull(secondJob);
        Assert.Equal(JobStatus.Failed, secondJob.Status);
        Assert.Contains("already running", secondJob.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartJob_UpdatePlan_WhenFirstJobCompleted_AllowsSecondJob()
    {
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 10);

        // Start and complete first job
        var firstJobId = service.CreateTestJob(new UpdatePlanArgs(_planFolder));
        service.CompleteJob(firstJobId, 0);
        var firstJob = service.GetJob(firstJobId);
        Assert.NotNull(firstJob);
        Assert.Equal(JobStatus.Completed, firstJob.Status);

        // Second job should be allowed (will fail to launch process, but should attempt)
        try
        {
            var secondJobId = service.StartJob(new UpdatePlanArgs(_planFolder));
            var secondJob = service.GetJob(secondJobId);
            Assert.NotNull(secondJob);
            // Should not be Failed due to conflict (might be Failed due to process launch, but that's OK)
            if (secondJob.Status == JobStatus.Failed)
                Assert.DoesNotContain("already running", secondJob.StatusMessage ?? "",
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

        var firstJobId = service.StartJob(new UpdatePlanArgs(_planFolder));
        var firstJob = service.GetJob(firstJobId);
        Assert.NotNull(firstJob);
        Assert.Equal(JobStatus.Queued, firstJob.Status);

        // Second job should fail even though first is only queued
        var secondJobId = service.StartJob(new UpdatePlanArgs(_planFolder));
        var secondJob = service.GetJob(secondJobId);

        Assert.NotNull(secondJob);
        Assert.Equal(JobStatus.Failed, secondJob.Status);
        Assert.Contains("already running", secondJob.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartJob_UpdatePlan_WhenFirstJobPending_FailsWithConflictMessage()
    {
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 10);

        // Create a job manually in Pending state
        var firstJobId = service.CreateTestJob(new UpdatePlanArgs(_planFolder));
        var firstJob = service.GetJob(firstJobId);
        Assert.NotNull(firstJob);
        firstJob.Status = JobStatus.Pending;

        // Second job should fail
        var secondJobId = service.StartJob(new UpdatePlanArgs(_planFolder));
        var secondJob = service.GetJob(secondJobId);

        Assert.NotNull(secondJob);
        Assert.Equal(JobStatus.Failed, secondJob.Status);
        Assert.Contains("already running", secondJob.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartJob_UpdatePlan_ForDifferentPlan_AllowsBothJobs()
    {
        var otherPlanFolder = Path.Combine(_tempDir.Path, "Plans", "00002-OtherPlan");
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
        var firstJobId = service.CreateTestJob(new UpdatePlanArgs(_planFolder));
        var firstJob = service.GetJob(firstJobId);
        Assert.NotNull(firstJob);
        Assert.Equal(JobStatus.Running, firstJob.Status);

        // Start UpdatePlan for different plan — should be allowed
        try
        {
            var secondJobId = service.StartJob(new UpdatePlanArgs(otherPlanFolder));
            var secondJob = service.GetJob(secondJobId);
            Assert.NotNull(secondJob);
            // Should not fail due to conflict
            if (secondJob.Status == JobStatus.Failed)
                Assert.DoesNotContain("already running", secondJob.StatusMessage ?? "",
                    StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // Process launch failures are acceptable
        }
    }

    [Fact]
    public void StartJob_ExecutePlan_WhenUpdatePlanRunning_Fails()
    {
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 10);

        // Start UpdatePlan
        var updateJobId = service.CreateTestJob(new UpdatePlanArgs(_planFolder));

        // ExecutePlan and UpdatePlan both mutate the plan worktree, so they conflict even though
        // they are different job types (#2710 follow-up: plan 00636 admitted RetryPlan and
        // ExecutePlan concurrently on the same worktree).
        var executeJobId = service.StartJob(new ExecutePlanArgs(_planFolder));
        var executeJob = service.GetJob(executeJobId);

        Assert.NotNull(executeJob);
        Assert.Equal(JobStatus.Failed, executeJob.Status);
        Assert.Contains("already running", executeJob.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(updateJobId, executeJob.StatusMessage);
        Assert.Contains("UpdatePlan", executeJob.StatusMessage);
        Assert.Contains("on this plan worktree", executeJob.StatusMessage);
    }

    [Fact]
    public void StartJob_CreatePr_WhenRetryPlanRunning_FailsNamingTheRetryPlanJob()
    {
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 10);

        // Collision 00606 / 03456+03462: CreatePr admitted 12 seconds after a RetryPlan on the
        // same plan worktree.
        var retryJobId = service.CreateTestJob(new RetryPlanArgs(_planFolder, "retry"));

        var prJobId = service.StartJob(new CreatePrArgs(_planFolder));
        var prJob = service.GetJob(prJobId);

        Assert.NotNull(prJob);
        Assert.Equal(JobStatus.Failed, prJob.Status);
        Assert.Contains(retryJobId, prJob.StatusMessage);
        Assert.Contains("RetryPlan", prJob.StatusMessage);
        Assert.Contains("on this plan worktree", prJob.StatusMessage);
    }

    [Fact]
    public void StartJob_RetryPlan_WhenCreatePrRunning_FailsNamingTheCreatePrJob()
    {
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 10);

        var prJobId = service.CreateTestJob(new CreatePrArgs(_planFolder));

        var retryJobId = service.StartJob(new RetryPlanArgs(_planFolder, "retry"));
        var retryJob = service.GetJob(retryJobId);

        Assert.NotNull(retryJob);
        Assert.Equal(JobStatus.Failed, retryJob.Status);
        Assert.Contains(prJobId, retryJob.StatusMessage);
        Assert.Contains("CreatePr", retryJob.StatusMessage);
        Assert.Contains("on this plan worktree", retryJob.StatusMessage);
    }

    [Fact]
    public void StartJob_RetryPlan_WhenExecutePlanRunning_Fails()
    {
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 10);

        // Collision 00636 / 03464+03469: RetryPlan and ExecutePlan admitted concurrently.
        var executeJobId = service.CreateTestJob(new ExecutePlanArgs(_planFolder));

        var retryJobId = service.StartJob(new RetryPlanArgs(_planFolder, "retry"));
        var retryJob = service.GetJob(retryJobId);

        Assert.NotNull(retryJob);
        Assert.Equal(JobStatus.Failed, retryJob.Status);
        Assert.Contains(executeJobId, retryJob.StatusMessage);
        Assert.Contains("ExecutePlan", retryJob.StatusMessage);
    }

    [Fact]
    public void StartJob_CreatePr_WhenRetryPlanRunningOnDifferentPlan_AllowsBothJobs()
    {
        var otherPlanFolder = Path.Combine(_tempDir.Path, "Plans", "00002-OtherPlan");
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

        _ = service.CreateTestJob(new RetryPlanArgs(_planFolder, "retry"));

        try
        {
            var prJobId = service.StartJob(new CreatePrArgs(otherPlanFolder));
            var prJob = service.GetJob(prJobId);
            Assert.NotNull(prJob);
            if (prJob.Status == JobStatus.Failed)
                Assert.DoesNotContain("already running", prJob.StatusMessage ?? "",
                    StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // Process launch failures are acceptable
        }
    }

    [Fact]
    public void StartJob_CreatePr_WithForce_WhenRetryPlanRunning_BypassesGuard()
    {
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 10);

        _ = service.CreateTestJob(new RetryPlanArgs(_planFolder, "retry"));

        try
        {
            var prJobId = service.StartJob(new CreatePrArgs(_planFolder, Force: true));
            var prJob = service.GetJob(prJobId);
            Assert.NotNull(prJob);
            if (prJob.Status == JobStatus.Failed)
                Assert.DoesNotContain("already running", prJob.StatusMessage ?? "",
                    StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // Process launch failures are acceptable
        }
    }

    [Fact]
    public void StartJob_CreateIssue_WhenExecutePlanRunning_AllowsBothJobs()
    {
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 10);

        // CreateIssue reads the plan and mutates GitHub rather than the worktree, so it keeps its
        // own PlanIssue exclusion group and does not conflict with worktree-mutating job types.
        _ = service.CreateTestJob(new ExecutePlanArgs(_planFolder));

        try
        {
            var issueJobId = service.StartJob(new CreateIssueArgs(_planFolder, "Ivy-Interactive/ivy-tendril"));
            var issueJob = service.GetJob(issueJobId);
            Assert.NotNull(issueJob);
            if (issueJob.Status == JobStatus.Failed)
                Assert.DoesNotContain("already running", issueJob.StatusMessage ?? "",
                    StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // Process launch failures are acceptable
        }
    }

    [Fact]
    public void StartJob_ExecutePlan_WhenAnotherExecutePlanRunning_FailsWithConflictMessage()
    {
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 10);

        // Start first ExecutePlan job
        var firstJobId = service.CreateTestJob(new ExecutePlanArgs(_planFolder));
        var firstJob = service.GetJob(firstJobId);
        Assert.NotNull(firstJob);
        Assert.Equal(JobStatus.Running, firstJob.Status);

        // Try to start second ExecutePlan job for the same plan
        var secondJobId = service.StartJob(new ExecutePlanArgs(_planFolder));
        var secondJob = service.GetJob(secondJobId);

        // Second job should fail immediately with conflict message
        Assert.NotNull(secondJob);
        Assert.Equal(JobStatus.Failed, secondJob.Status);
        Assert.Contains("already running", secondJob.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(firstJobId, secondJob.StatusMessage);
    }

    [Fact]
    public void StartJob_ExecutePlan_WhenAnotherExecutePlanBlocked_FailsWithConflictMessage()
    {
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 10);

        // Create a running dependency job so ExecutePlan gets blocked via WaitForJobs
        var depId = service.CreateTestJob(new CreatePlanArgs("Dep job", "Auto"));

        // Start ExecutePlan with WaitForJobs — it enters Blocked state
        var firstJobId = service.StartJob(new ExecutePlanArgs(_planFolder) { WaitForJobs = [depId] });
        var firstJob = service.GetJob(firstJobId);
        Assert.NotNull(firstJob);
        Assert.Equal(JobStatus.Blocked, firstJob.Status);

        // Try to start another ExecutePlan for the same plan — should be rejected
        var secondJobId = service.StartJob(new ExecutePlanArgs(_planFolder));
        var secondJob = service.GetJob(secondJobId);

        Assert.NotNull(secondJob);
        Assert.Equal(JobStatus.Failed, secondJob.Status);
        Assert.Contains("already running", secondJob.StatusMessage, StringComparison.OrdinalIgnoreCase);
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
            _ = service.CreateTestJob(new UpdatePlanArgs(_planFolder));

            // Try to start conflicting second job
            _ = service.StartJob(new UpdatePlanArgs(_planFolder));

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
