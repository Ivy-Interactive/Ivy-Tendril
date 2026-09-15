using Ivy.Tendril.Agents.Providers.Antigravity;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Jobs;

namespace Ivy.Tendril.Test.Services;

/// <summary>
///     A fleet of queued jobs against an exhausted quota should cost a few failures and one
///     notification, not one failure per job.
/// </summary>
public class ProviderCircuitBreakerTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    private const string QuotaMessage =
        "antigravity/gemini-3.8-flash: provider quota exhausted — RESOURCE_EXHAUSTED (code 429)";

    private const string FailedResultLine =
        "{\"event\":\"result\",\"result\":{\"status\":\"ERROR\"," +
        "\"error\":\"API error (attempt 5): RESOURCE_EXHAUSTED (code 429): Resource has been exhausted (e.g. check quota).\"}}";

    [Fact]
    public void IsOpen_AfterThreeConsecutiveQuotaFailures_ReturnsTrue()
    {
        var breaker = new ProviderCircuitBreaker();

        Assert.False(breaker.RecordFailure("antigravity", QuotaMessage));
        Assert.False(breaker.IsOpen("antigravity", out _));

        Assert.False(breaker.RecordFailure("antigravity", QuotaMessage));
        Assert.False(breaker.IsOpen("antigravity", out _));

        // The third is the one that trips it, and the only one that asks for a notification.
        Assert.True(breaker.RecordFailure("antigravity", QuotaMessage));
        Assert.True(breaker.IsOpen("antigravity", out var reason));
        Assert.Contains("antigravity", reason);
        Assert.Contains("RESOURCE_EXHAUSTED", reason);

        // A fourth failure while open must not ask for a second notification.
        Assert.False(breaker.RecordFailure("antigravity", QuotaMessage));
    }

    [Fact]
    public void IsOpen_OtherProvider_IsUnaffected()
    {
        var breaker = new ProviderCircuitBreaker();

        for (var i = 0; i < ProviderCircuitBreaker.FailureThreshold; i++)
            breaker.RecordFailure("antigravity", QuotaMessage);

        Assert.True(breaker.IsOpen("antigravity", out _));
        Assert.False(breaker.IsOpen("claude", out _));
    }

    [Fact]
    public void RecordSuccess_ResetsTheStreak()
    {
        var breaker = new ProviderCircuitBreaker();

        breaker.RecordFailure("antigravity", QuotaMessage);
        breaker.RecordFailure("antigravity", QuotaMessage);
        breaker.RecordSuccess("antigravity");

        // Back to zero, so the next failure is the first of a new streak rather than the third.
        Assert.False(breaker.RecordFailure("antigravity", QuotaMessage));
        Assert.False(breaker.IsOpen("antigravity", out _));
    }

    [Fact]
    public void RecordSuccess_WhileOpen_ClosesTheBreaker()
    {
        var breaker = new ProviderCircuitBreaker();

        for (var i = 0; i < ProviderCircuitBreaker.FailureThreshold; i++)
            breaker.RecordFailure("antigravity", QuotaMessage);
        Assert.True(breaker.IsOpen("antigravity", out _));

        breaker.RecordSuccess("antigravity");

        Assert.False(breaker.IsOpen("antigravity", out _));
    }

    // Injected clock, because a real ten-minute cooldown is not a thing a test can wait for.
    [Fact]
    public void IsOpen_AfterCooldown_HalfOpens()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var breaker = new ProviderCircuitBreaker(() => now);

        for (var i = 0; i < ProviderCircuitBreaker.FailureThreshold; i++)
            breaker.RecordFailure("antigravity", QuotaMessage);

        now += ProviderCircuitBreaker.Cooldown - TimeSpan.FromSeconds(1);
        Assert.True(breaker.IsOpen("antigravity", out _));

        now += TimeSpan.FromSeconds(2);
        Assert.False(breaker.IsOpen("antigravity", out _));

        // Half-open means the streak restarted too, so one failure after the cooldown must not re-trip
        // on a count that predates it.
        Assert.False(breaker.RecordFailure("antigravity", QuotaMessage));
        Assert.False(breaker.IsOpen("antigravity", out _));
    }

    [Fact]
    public void RecordFailure_NoProvider_IsIgnored()
    {
        var breaker = new ProviderCircuitBreaker();

        Assert.False(breaker.RecordFailure(null, QuotaMessage));
        Assert.False(breaker.RecordFailure("", QuotaMessage));
        Assert.False(breaker.IsOpen(null, out _));
    }

    // The whole point of the breaker: the queue stops draining into the wall, and the operator is told
    // once. Three jobs die of the quota, the fourth never launches.
    [Fact]
    public void ProcessJobQueue_OpenBreaker_LeavesJobQueued()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        using var service = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10));

        var notifications = new List<JobNotification>();
        service.NotificationReady += n => notifications.Add(n);

        for (var i = 0; i < ProviderCircuitBreaker.FailureThreshold; i++)
        {
            var id = service.CreateTestJob(new ExecutePlanArgs(_tempDir.Path));
            var failing = service.GetJob(id)!;
            failing.Provider = "antigravity";
            failing.EventParser = new AntigravityEventParser();
            failing.EnqueueOutput(FailedResultLine);
            service.CompleteJob(id, 1);
            Assert.Equal(JobStatus.Failed, service.GetJob(id)!.Status);
        }

        Assert.True(service.ProviderBreaker.IsOpen("antigravity", out _));
        var unavailable = notifications.Where(n => n.Title.Contains("Unavailable", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(unavailable);
        Assert.Contains("antigravity", unavailable[0].Message);

        var queuedId = service.CreateTestJob(new ExecutePlanArgs(_tempDir.Path));
        var queuedJob = service.GetJob(queuedId)!;
        queuedJob.Provider = "antigravity";
        var slotsBefore = service.LocalSlotsInUse;

        service.EnqueueAndPumpQueue(queuedJob, "Waiting");

        Assert.Equal(JobStatus.Queued, queuedJob.Status);
        Assert.Contains("antigravity", queuedJob.StatusMessage);
        // The refused admission gave its slot back, so pumping the queue does not leak one per attempt.
        service.EnqueueAndPumpQueue(queuedJob, "Waiting");
        Assert.Equal(slotsBefore, service.LocalSlotsInUse);

        // Still exactly one notification, however many times the queue is pumped against the wall.
        Assert.Single(notifications.Where(n => n.Title.Contains("Unavailable", StringComparison.OrdinalIgnoreCase)));
    }
}
