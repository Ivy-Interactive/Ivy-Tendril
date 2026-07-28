using System.Collections.Concurrent;
using System.Reflection;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

public class JobServiceStopQueuedTests
{
    private static JobService CreateService()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        return new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10));
    }

    private static void AddJobDirectly(JobService service, JobItem job)
    {
        var field = typeof(JobService).GetField("_jobs",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var jobs = (ConcurrentDictionary<string, JobItem>)field!.GetValue(service)!;
        jobs[job.Id] = job;
    }

    [Fact]
    public void StopQueuedJobs_StopsOnlyQueuedJobs()
    {
        var service = CreateService();
        AddJobDirectly(service, new JobItem { Id = "queued-1", Status = JobStatus.Queued });
        AddJobDirectly(service, new JobItem { Id = "queued-2", Status = JobStatus.Queued });
        AddJobDirectly(service, new JobItem { Id = "queued-3", Status = JobStatus.Queued });
        AddJobDirectly(service, new JobItem { Id = "running-1", Status = JobStatus.Running });
        AddJobDirectly(service, new JobItem { Id = "completed-1", Status = JobStatus.Completed });

        var stoppedCount = service.StopQueuedJobs();

        Assert.Equal(3, stoppedCount);
        Assert.Equal(JobStatus.Stopped, service.GetJob("queued-1")!.Status);
        Assert.Equal(JobStatus.Stopped, service.GetJob("queued-2")!.Status);
        Assert.Equal(JobStatus.Stopped, service.GetJob("queued-3")!.Status);
        Assert.Equal(JobStatus.Running, service.GetJob("running-1")!.Status);
        Assert.Equal(JobStatus.Completed, service.GetJob("completed-1")!.Status);
    }

    [Fact]
    public void StopQueuedJobs_NoQueuedJobs_ReturnsZeroAndRaisesNoEvent()
    {
        var service = CreateService();
        AddJobDirectly(service, new JobItem { Id = "running-1", Status = JobStatus.Running });
        AddJobDirectly(service, new JobItem { Id = "completed-1", Status = JobStatus.Completed });

        var structureChangedCount = 0;
        service.JobsStructureChanged += () => structureChangedCount++;

        var stoppedCount = service.StopQueuedJobs();

        Assert.Equal(0, stoppedCount);
        Assert.Equal(0, structureChangedCount);
    }

    [Fact]
    public void StopQueuedJobs_RaisesStructureChangedExactlyOnce()
    {
        var service = CreateService();
        AddJobDirectly(service, new JobItem { Id = "queued-1", Status = JobStatus.Queued });
        AddJobDirectly(service, new JobItem { Id = "queued-2", Status = JobStatus.Queued });
        AddJobDirectly(service, new JobItem { Id = "queued-3", Status = JobStatus.Queued });

        var structureChangedCount = 0;
        service.JobsStructureChanged += () => structureChangedCount++;

        service.StopQueuedJobs();

        Assert.Equal(1, structureChangedCount);
    }

    [Fact]
    public void StopQueuedJobs_LeavesJobsVisibleUntilCleared()
    {
        var service = CreateService();
        AddJobDirectly(service, new JobItem { Id = "queued-1", Status = JobStatus.Queued });
        AddJobDirectly(service, new JobItem { Id = "queued-2", Status = JobStatus.Queued });

        service.StopQueuedJobs();

        Assert.NotNull(service.GetJob("queued-1"));
        Assert.NotNull(service.GetJob("queued-2"));

        service.ClearAllJobs();

        Assert.Null(service.GetJob("queued-1"));
        Assert.Null(service.GetJob("queued-2"));
    }
}
