using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Test.Helpers;

namespace Ivy.Tendril.Test;

/// <summary>
///     Covers the rate-limit cooldown: a job the provider rejected for a rate limit or an exhausted
///     daily quota is parked as Blocked, the queue is held back for the cooldown, and the job is
///     auto-restarted once the cooldown expires (issue #1756).
/// </summary>
public class JobServiceRateLimitTests : IDisposable
{
    private const string DailyQuotaStderr =
        "[stderr] API Error: Request rejected (429) - Too many tokens per day, please wait before trying again.";

    private const string ShortTermStderr = "[stderr] rate limit exceeded";

    private readonly TempDirectoryFixture _tempDir = new();

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    private string CreatePlanFolder(string name)
    {
        var dir = Path.Combine(_tempDir.Path, $"{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var repoDir = Path.Combine(dir, "repo");
        Directory.CreateDirectory(repoDir);
        File.WriteAllText(Path.Combine(dir, "plan.yaml"),
            $"state: Executing\nproject: TestProject\nlevel: NiceToHave\ntitle: {name}\ncreated: 2026-01-01T00:00:00Z\nupdated: 2026-01-01T00:00:00Z\nrepos:\n- {repoDir}\nprs: []\ncommits: []\nverifications: []\nrelatedPlans: []\ndependsOn: []\n");
        return dir;
    }

    private static JobService CreateService(
        TimeSpan? cooldown = null,
        TimeSpan? dailyCooldown = null,
        int maxRetries = 3)
    {
        SynchronizationContext.SetSynchronizationContext(null);
        return new JobService(
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(10),
            agentRunner: TestAgentRunner.Create(),
            rateLimitCooldown: cooldown ?? TimeSpan.FromMinutes(5),
            rateLimitDailyCooldown: dailyCooldown ?? TimeSpan.FromMinutes(60),
            rateLimitMaxRetries: maxRetries);
    }

    /// <summary>Runs a job to a rate-limit failure and returns its id.</summary>
    private static string FailWithRateLimit(JobService service, string planFolder, string stderrLine)
    {
        var id = service.CreateTestJob(new ExecutePlanArgs(planFolder));
        service.GetJob(id)!.OutputLines.Enqueue(stderrLine);
        service.CompleteJob(id, 1);
        return id;
    }

    [Fact]
    public void CompleteJob_DailyQuotaFailure_ParksJobWithCooldownInsteadOfFailing()
    {
        var planFolder = CreatePlanFolder("plan-quota");
        var service = CreateService(dailyCooldown: TimeSpan.FromMinutes(60));

        var notifications = new List<JobNotification>();
        service.NotificationReady += n => notifications.Add(n);

        var before = DateTime.UtcNow;
        var id = FailWithRateLimit(service, planFolder, DailyQuotaStderr);

        var job = service.GetJob(id)!;
        Assert.Equal(JobStatus.Blocked, job.Status);
        Assert.NotNull(job.RateLimitedUntil);
        Assert.True(job.RateLimitedUntil > before.AddMinutes(59), "daily quota should use the long cooldown");
        Assert.Contains("auto-retry after", job.StatusMessage);
        Assert.Contains("attempt 1/3", job.StatusMessage);
        Assert.Equal(0, job.RateLimitRetries);

        // The plan must not go back to Draft: it is still ours, it just has to wait.
        var planState = JobService.ReadPlanYaml(planFolder)!.State;
        Assert.Equal(nameof(PlanStatus.Blocked), planState);

        Assert.Contains(notifications, n => n.Title == "ExecutePlan Rate Limited");
    }

    [Fact]
    public void StartJob_WhileRateLimitPaused_QueuesInsteadOfRunning()
    {
        var parkedPlan = CreatePlanFolder("plan-parked");
        var nextPlan = CreatePlanFolder("plan-next");
        var service = CreateService(cooldown: TimeSpan.FromMinutes(5));

        FailWithRateLimit(service, parkedPlan, ShortTermStderr);

        var queuedId = service.StartJob(new ExecutePlanArgs(nextPlan));

        var queued = service.GetJob(queuedId)!;
        Assert.Equal(JobStatus.Queued, queued.Status);
        Assert.StartsWith("Paused: provider rate limited until", queued.StatusMessage);

        // Draining the queue must not release it while the cooldown is still active.
        service.RunBlockedJobCheck();
        Assert.Equal(JobStatus.Queued, service.GetJob(queuedId)!.Status);
    }

    [Fact]
    public void RunBlockedJobCheck_AfterCooldownElapsed_RestartsParkedJobAndDrainsQueue()
    {
        var parkedPlan = CreatePlanFolder("plan-parked");
        var nextPlan = CreatePlanFolder("plan-next");
        // A short cooldown keeps the test fast while still exercising a genuinely active pause.
        var service = CreateService(cooldown: TimeSpan.FromMilliseconds(750));

        var parkedId = FailWithRateLimit(service, parkedPlan, ShortTermStderr);
        var parked = service.GetJob(parkedId)!;
        Assert.Equal(JobStatus.Blocked, parked.Status);

        var queuedId = service.StartJob(new ExecutePlanArgs(nextPlan));
        Assert.Equal(JobStatus.Queued, service.GetJob(queuedId)!.Status);

        Assert.True(
            RetryHelper.WaitUntil(() => DateTime.UtcNow > parked.RateLimitedUntil!.Value, TimeSpan.FromSeconds(10)),
            "cooldown did not elapse");

        service.RunBlockedJobCheck();

        // The parked job is replaced by a fresh attempt that carries the retry count forward.
        Assert.Null(service.GetJob(parkedId));
        var replacement = service.GetJobs()
            .FirstOrDefault(j => j.Id != parkedId && j.TypedArgs?.PlanFolder == parkedPlan);
        Assert.NotNull(replacement);
        Assert.Equal(1, replacement.RateLimitRetries);
        Assert.NotEqual(JobStatus.Blocked, replacement.Status);

        Assert.True(
            RetryHelper.WaitUntil(() => service.GetJob(queuedId)!.Status != JobStatus.Queued),
            "queued job was not drained after the cooldown");
    }

    [Fact]
    public void CompleteJob_WhenRetriesExhausted_LeavesJobFailed()
    {
        var planFolder = CreatePlanFolder("plan-exhausted");
        var service = CreateService(maxRetries: 3);

        var id = service.CreateTestJob(new ExecutePlanArgs(planFolder));
        var job = service.GetJob(id)!;
        job.RateLimitRetries = 3;
        job.OutputLines.Enqueue(ShortTermStderr);

        service.CompleteJob(id, 1);

        job = service.GetJob(id)!;
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Null(job.RateLimitedUntil);
        Assert.Contains("gave up after 3 rate-limit retries", job.StatusMessage);

        // No replacement attempt: the cap is the end of the line.
        Assert.Single(service.GetJobs(), j => j.TypedArgs?.PlanFolder == planFolder);
    }

    [Fact]
    public void CompleteJob_WhenMaxRetriesIsZero_FailsImmediately()
    {
        var planFolder = CreatePlanFolder("plan-disabled");
        var service = CreateService(maxRetries: 0);

        var id = FailWithRateLimit(service, planFolder, DailyQuotaStderr);

        var job = service.GetJob(id)!;
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Null(job.RateLimitedUntil);
        Assert.DoesNotContain("auto-retry", job.StatusMessage);

        // Auto-retry off means the pause is off too: the next job runs right away.
        var nextId = service.StartJob(new ExecutePlanArgs(CreatePlanFolder("plan-next")));
        Assert.NotEqual(JobStatus.Queued, service.GetJob(nextId)!.Status);
    }

    [Fact]
    public void ForceStartJob_OnRateLimitParkedJob_StartsDespiteThePause()
    {
        var planFolder = CreatePlanFolder("plan-forced");
        var service = CreateService(cooldown: TimeSpan.FromMinutes(5));

        var parkedId = FailWithRateLimit(service, planFolder, ShortTermStderr);
        Assert.Equal(JobStatus.Blocked, service.GetJob(parkedId)!.Status);

        service.ForceStartJob(parkedId);

        Assert.Null(service.GetJob(parkedId));
        var forced = service.GetJobs().FirstOrDefault(j => j.TypedArgs?.PlanFolder == planFolder);
        Assert.NotNull(forced);
        Assert.NotEqual(JobStatus.Blocked, forced.Status);
        Assert.NotEqual(JobStatus.Queued, forced.Status);
    }

    [Fact]
    public void RunBlockedJobCheck_DependencyBlockedJob_IsStillAutoRestarted()
    {
        // Regression guard for the RetryBlockedJobs skip: it must only skip rate-limit-parked jobs
        // (RateLimitedUntil set), leaving ordinary dependency-blocked jobs to be retried as before.
        var parkedPlan = CreatePlanFolder("plan-parked");
        var dependencyPlan = CreatePlanFolder("plan-dependency");
        var service = CreateService(cooldown: TimeSpan.FromMinutes(5));

        var parkedId = FailWithRateLimit(service, parkedPlan, ShortTermStderr);
        var parkedUntil = service.GetJob(parkedId)!.RateLimitedUntil;

        var blockedId = service.CreateTestJob(new ExecutePlanArgs(dependencyPlan));
        var blockedJob = service.GetJob(blockedId)!;
        blockedJob.Status = JobStatus.Blocked;
        blockedJob.StatusMessage = "Dependency '01100-DepPlan' is 'Executing', not Completed";

        var notifications = new List<JobNotification>();
        service.NotificationReady += n => notifications.Add(n);

        service.RunBlockedJobCheck();

        // The dependency-blocked job was restarted...
        Assert.Null(service.GetJob(blockedId));
        var restarted = service.GetJobs()
            .FirstOrDefault(j => j.Id != blockedId && j.TypedArgs?.PlanFolder == dependencyPlan);
        Assert.NotNull(restarted);
        Assert.Contains(notifications, n => n.Title == "Job Unblocked");

        // ...while the rate-limited job stayed parked, waiting out its cooldown.
        var stillParked = service.GetJob(parkedId);
        Assert.NotNull(stillParked);
        Assert.Equal(JobStatus.Blocked, stillParked.Status);
        Assert.Equal(parkedUntil, stillParked.RateLimitedUntil);
    }
}
