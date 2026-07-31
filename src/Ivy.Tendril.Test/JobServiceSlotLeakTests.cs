using System.Collections.Concurrent;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Jobs;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test;

// Regression coverage for #1564: an unhandled exception during JobLauncher.LaunchJob used to leak
// the acquired concurrency-slot permit, eventually draining _jobSlotSemaphore and stranding every
// subsequent job in Queued forever.
public class JobServiceSlotLeakTests
{
    private static JobLauncher CreateLauncher() =>
        new(configService: null, agentRunner: null, NullLogger.Instance, promptsRoot: "");

    private static JobItem CreateJob(string id) => new()
    {
        Id = id,
        Type = "CreatePlan",
        TypedArgs = new CreatePlanArgs("Test job", "Auto"),
        Status = JobStatus.Pending
    };

    [Fact]
    public void LaunchJob_WhenBeforeHookThrowsUnhandled_MarksJobFailedAndReleasesSlot()
    {
        var launcher = CreateLauncher();
        var jobs = new ConcurrentDictionary<string, JobItem>();
        using var semaphore = new SemaphoreSlim(1, 1);
        semaphore.Wait(); // simulate the slot StartJobInternal already acquired before calling LaunchJob

        var job = CreateJob("job-1");
        jobs[job.Id] = job;
        var structureChangedCount = 0;

        // RunHooks ("before"-hook) is a genuinely unhandled throw site per the plan — it is not one
        // of the three existing guarded failure paths in JobLauncher.
        launcher.LaunchJob(
            job, jobs, semaphore, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            runHooks: (_, _, _, _, _) => throw new InvalidOperationException("boom from before-hook"),
            completeJob: (_, _, _, _) => { },
            raiseStructureChanged: () => structureChangedCount++);

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Contains("boom from before-hook", job.StatusMessage);
        Assert.Equal(1, semaphore.CurrentCount);
        Assert.True(structureChangedCount > 0);
    }

    [Fact]
    public void LaunchJob_RepeatedUnhandledFailures_DoNotPermanentlyDrainSemaphore()
    {
        var launcher = CreateLauncher();
        using var semaphore = new SemaphoreSlim(1, 1);

        for (var i = 0; i < 3; i++)
        {
            // Mirrors StartJobInternal/ProcessJobQueue: acquire the slot before launching.
            Assert.True(semaphore.Wait(0));

            var jobs = new ConcurrentDictionary<string, JobItem>();
            var job = CreateJob($"job-{i}");
            jobs[job.Id] = job;

            launcher.LaunchJob(
                job, jobs, semaphore, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
                runHooks: (_, _, _, _, _) => throw new InvalidOperationException("boom"),
                completeJob: (_, _, _, _) => { },
                raiseStructureChanged: () => { });

            Assert.Equal(JobStatus.Failed, job.Status);
        }

        // If any of the 3 failures above had leaked its permit, the semaphore (capacity 1) would
        // already be fully drained and this would time out.
        Assert.True(semaphore.Wait(0));
        semaphore.Release();
    }

    [Fact]
    public void LaunchJob_HandledGuardFailure_ReleasesSlotExactlyOnce()
    {
        // configService/agentRunner are null, so TryBuildAgentProcessStart short-circuits to
        // (null, null) and launch fails via the existing handled "no agent program found" guard.
        var launcher = CreateLauncher();
        var jobs = new ConcurrentDictionary<string, JobItem>();
        using var semaphore = new SemaphoreSlim(1, 1);
        semaphore.Wait();

        var job = CreateJob("job-handled");
        jobs[job.Id] = job;

        launcher.LaunchJob(
            job, jobs, semaphore, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            runHooks: (_, _, _, _, _) => (false, null),
            completeJob: (_, _, _, _) => { },
            raiseStructureChanged: () => { });

        Assert.Equal(JobStatus.Failed, job.Status);
        // SemaphoreSlim.Release() throws SemaphoreFullException on over-release, so reaching this
        // assertion with CurrentCount == 1 (not more) confirms exactly one release occurred.
        Assert.Equal(1, semaphore.CurrentCount);
    }
}
