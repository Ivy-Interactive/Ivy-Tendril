using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

public class JobServiceStopAllTests
{
    [Fact]
    public void StopAllJobs_StopsAllQueuedJobs()
    {
        // maxConcurrentJobs=0 means all jobs get queued
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 0);

        service.StartJob(new CreatePlanArgs("Job 1", "Auto"));
        service.StartJob(new CreatePlanArgs("Job 2", "Auto"));
        service.StartJob(new CreatePlanArgs("Job 3", "Auto"));

        var stoppedCount = service.StopAllJobs();

        Assert.Equal(3, stoppedCount);
        Assert.All(service.GetJobs(), j => Assert.Equal(JobStatus.Stopped, j.Status));
    }

    [Fact]
    public void StopAllJobs_WithNoJobs_ReturnsZero()
    {
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 0);

        var stoppedCount = service.StopAllJobs();

        Assert.Equal(0, stoppedCount);
    }

    [Fact]
    public void StopAllJobs_StopsBlockedJobs()
    {
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 0);

        var id = service.CreateTestJob(new CreatePlanArgs("Blocked Job", "Auto"));
        service.GetJob(id)!.Status = JobStatus.Blocked;

        var stoppedCount = service.StopAllJobs();

        Assert.Equal(1, stoppedCount);
        Assert.Equal(JobStatus.Stopped, service.GetJob(id)!.Status);
    }

    [Fact]
    public void StopAllJobs_LeavesTerminalJobsUntouched()
    {
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 0);

        var completedId = service.CreateTestJob(new CreatePlanArgs("Completed Job", "Auto"));
        service.GetJob(completedId)!.Status = JobStatus.Completed;

        service.StartJob(new CreatePlanArgs("Queued Job", "Auto"));

        var stoppedCount = service.StopAllJobs();

        Assert.Equal(1, stoppedCount);
        Assert.Equal(JobStatus.Completed, service.GetJob(completedId)!.Status);
    }

    [Fact]
    public void StopAllJobs_IsIdempotent()
    {
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 0);

        service.StartJob(new CreatePlanArgs("Job 1", "Auto"));
        service.StartJob(new CreatePlanArgs("Job 2", "Auto"));

        var firstCount = service.StopAllJobs();
        Assert.Equal(2, firstCount);

        var statusesAfterFirst = service.GetJobs().Select(j => j.Status).ToList();

        var secondCount = service.StopAllJobs();

        Assert.Equal(0, secondCount);
        Assert.Equal(statusesAfterFirst, service.GetJobs().Select(j => j.Status).ToList());
    }
}
