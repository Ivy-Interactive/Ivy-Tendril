using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Jobs;

namespace Ivy.Tendril.Test;

public class JobServiceWaitForJobsTests
{
    [Fact]
    public void StartJob_WithWaitForJobs_BlocksWhenDependencyRunning()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            maxConcurrentJobs: 5);

        var depId = service.CreateTestJob(new CreatePlanArgs("Dep job", "Auto"));
        Assert.Equal(JobStatus.Running, service.GetJob(depId)!.Status);

        var id = service.StartJob(new CreatePlanArgs("Waiting job", "Auto") { WaitForJobs = [depId] });
        var job = service.GetJob(id);

        Assert.NotNull(job);
        Assert.Equal(JobStatus.Blocked, job.Status);
        Assert.Contains(depId, job.StatusMessage);
    }

    [Fact]
    public void StartJob_WithWaitForJobs_DoesNotBlockWhenAllCompleted()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            maxConcurrentJobs: 5);

        var depId = service.CreateTestJob(new CreatePlanArgs("Dep job", "Auto"));
        service.CompleteJob(depId, 0);

        var id = service.StartJob(new CreatePlanArgs("Waiting job", "Auto") { WaitForJobs = [depId] });
        var job = service.GetJob(id);

        Assert.NotNull(job);
        Assert.NotEqual(JobStatus.Blocked, job.Status);
    }

    [Fact]
    public void StartJob_WithWaitForJobs_FailsImmediatelyWhenDependencyAlreadyFailed()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            maxConcurrentJobs: 5);

        var depId = service.CreateTestJob(new CreatePlanArgs("Dep job", "Auto"));
        service.CompleteJob(depId, 1);

        var id = service.StartJob(new CreatePlanArgs("Waiting job", "Auto") { WaitForJobs = [depId] });
        var job = service.GetJob(id);

        Assert.NotNull(job);
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Contains(depId, job.StatusMessage);
    }

    [Fact]
    public void CompleteJob_Success_UnblocksWaitingJobs()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            maxConcurrentJobs: 5);

        var depId = service.CreateTestJob(new CreatePlanArgs("Dep job", "Auto"));

        var waitingId = service.StartJob(new CreatePlanArgs("Waiting job", "Auto") { WaitForJobs = [depId] });
        Assert.Equal(JobStatus.Blocked, service.GetJob(waitingId)!.Status);

        var notifications = new List<JobNotification>();
        service.NotificationReady += n => notifications.Add(n);

        service.CompleteJob(depId, 0);

        // The blocked job should have been removed (restarted as a new job)
        Assert.Null(service.GetJob(waitingId));
        Assert.Contains(notifications, n => n.Title == "Job Unblocked");
    }

    [Fact]
    public void CompleteJob_Failure_CascadesFailureToWaitingJobs()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            maxConcurrentJobs: 5);

        var depId = service.CreateTestJob(new CreatePlanArgs("Dep job", "Auto"));

        var waitingId = service.StartJob(new CreatePlanArgs("Waiting job", "Auto") { WaitForJobs = [depId] });
        Assert.Equal(JobStatus.Blocked, service.GetJob(waitingId)!.Status);

        var notifications = new List<JobNotification>();
        service.NotificationReady += n => notifications.Add(n);

        service.CompleteJob(depId, 1);

        var waitingJob = service.GetJob(waitingId);
        Assert.NotNull(waitingJob);
        Assert.Equal(JobStatus.Failed, waitingJob.Status);
        Assert.Contains(depId, waitingJob.StatusMessage);
        Assert.Contains(notifications, n => n.Title == "Job Failed");
    }

    [Fact]
    public void CompleteJob_Failure_CascadesTransitively()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            maxConcurrentJobs: 5);

        // A -> B -> C chain
        var jobAId = service.CreateTestJob(new CreatePlanArgs("Job A", "Auto"));

        var jobBId = service.StartJob(new CreatePlanArgs("Job B", "Auto") { WaitForJobs = [jobAId] });
        Assert.Equal(JobStatus.Blocked, service.GetJob(jobBId)!.Status);

        var jobCId = service.StartJob(new CreatePlanArgs("Job C", "Auto") { WaitForJobs = [jobBId] });
        Assert.Equal(JobStatus.Blocked, service.GetJob(jobCId)!.Status);

        // Fail A -> should cascade to B -> should cascade to C
        service.CompleteJob(jobAId, 1);

        var jobB = service.GetJob(jobBId);
        var jobC = service.GetJob(jobCId);

        Assert.NotNull(jobB);
        Assert.Equal(JobStatus.Failed, jobB.Status);

        Assert.NotNull(jobC);
        Assert.Equal(JobStatus.Failed, jobC.Status);
    }

    [Fact]
    public void StopJob_CascadesFailureToWaitingJobs()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            maxConcurrentJobs: 5);

        var depId = service.CreateTestJob(new CreatePlanArgs("Dep job", "Auto"));

        var waitingId = service.StartJob(new CreatePlanArgs("Waiting job", "Auto") { WaitForJobs = [depId] });
        Assert.Equal(JobStatus.Blocked, service.GetJob(waitingId)!.Status);

        service.StopJob(depId);

        var waitingJob = service.GetJob(waitingId);
        Assert.NotNull(waitingJob);
        Assert.Equal(JobStatus.Failed, waitingJob.Status);
        Assert.Contains(depId, waitingJob.StatusMessage);
    }

    [Fact]
    public void CompleteJob_MultipleWaitForJobs_UnblocksOnlyWhenAllComplete()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            maxConcurrentJobs: 5);

        var dep1Id = service.CreateTestJob(new CreatePlanArgs("Dep 1", "Auto"));
        var dep2Id = service.CreateTestJob(new CreatePlanArgs("Dep 2", "Auto"));

        var waitingId = service.StartJob(new CreatePlanArgs("Waiting job", "Auto") { WaitForJobs = [dep1Id, dep2Id] });
        Assert.Equal(JobStatus.Blocked, service.GetJob(waitingId)!.Status);

        // Complete first dep — should still be blocked
        service.CompleteJob(dep1Id, 0);
        var stillBlocked = service.GetJob(waitingId);
        Assert.NotNull(stillBlocked);
        Assert.Equal(JobStatus.Blocked, stillBlocked.Status);

        // Complete second dep — should now unblock
        service.CompleteJob(dep2Id, 0);
        Assert.Null(service.GetJob(waitingId));
    }

    [Fact]
    public void StartJob_WithoutWaitForJobs_NotBlocked()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            maxConcurrentJobs: 5);

        var id = service.StartJob(new CreatePlanArgs("Normal job", "Auto"));
        var job = service.GetJob(id);

        Assert.NotNull(job);
        Assert.NotEqual(JobStatus.Blocked, job.Status);
    }
}
