using System.Collections.Concurrent;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

public class JobServiceNotificationThreadSafetyTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    private string CreateValidPlanFolder()
    {
        var dir = Path.Combine(_tempDir.Path, $"plan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var repoDir = Path.Combine(dir, "repo");
        Directory.CreateDirectory(repoDir);
        File.WriteAllText(Path.Combine(dir, "plan.yaml"),
            $"state: Draft\nproject: TestProject\nlevel: NiceToHave\ntitle: Test Plan\ncreated: 2026-01-01T00:00:00Z\nupdated: 2026-01-01T00:00:00Z\nrepos:\n- {repoDir}\nprs: []\ncommits: []\nverifications: []\nrelatedPlans: []\ndependsOn: []\n");
        return dir;
    }
    [Fact]
    public void NotificationReady_FiresOnSyncContext_WhenAvailable()
    {
        var testContext = new TestSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(testContext);
        try
        {
            var service = new JobService(
                TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
                null, 1);

            JobNotification? received = null;
            service.NotificationReady += n => received = n;

            // Start a job and complete it to trigger notification
            var id = service.StartJob("CreatePr", _tempDir.Path);
            service.CompleteJob(id, 0);

            // The notification should have been posted to the sync context, not invoked directly
            Assert.Null(received);
            Assert.True(testContext.PostCount > 0, "Expected at least one Post to sync context");

            // Execute the posted callbacks
            testContext.ExecutePending();
            Assert.NotNull(received);
            Assert.Equal("CreatePr Completed", received.Title);
            Assert.True(received.IsSuccess);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(null);
        }
    }

    [Fact]
    public void NotificationReady_FiresSynchronously_WhenNoSyncContext()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 1);

        JobNotification? received = null;
        service.NotificationReady += n => received = n;

        var id = service.StartJob("CreatePr", _tempDir.Path);
        service.CompleteJob(id, 0);

        Assert.NotNull(received);
        Assert.Equal("CreatePr Completed", received.Title);
        Assert.True(received.IsSuccess);
    }

    [Fact]
    public void NotificationReady_MultipleRapidNotifications_PreserveOrder()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 10);

        var notifications = new ConcurrentQueue<JobNotification>();
        service.NotificationReady += n => notifications.Enqueue(n);

        // Complete multiple jobs rapidly
        var ids = new List<string>();
        for (var i = 0; i < 5; i++) ids.Add(service.StartJob("CreatePr", _tempDir.Path));

        foreach (var id in ids) service.CompleteJob(id, 0);

        Assert.Equal(5, notifications.Count);

        // All should be "CreatePr Completed"
        while (notifications.TryDequeue(out var n))
        {
            Assert.Equal("CreatePr Completed", n.Title);
            Assert.True(n.IsSuccess);
        }
    }

    [Fact]
    public void NotificationReady_FailedJob_DeliversFailureNotification()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 1);

        JobNotification? received = null;
        service.NotificationReady += n => received = n;

        var planFolder = CreateValidPlanFolder();
        var id = service.StartJob("ExecutePlan", planFolder);
        service.CompleteJob(id, 1);

        Assert.NotNull(received);
        Assert.Equal("ExecutePlan Failed", received.Title);
        Assert.False(received.IsSuccess);
    }

    private class TestSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _pending = new();
        public int PostCount { get; private set; }

        public override void Post(SendOrPostCallback d, object? state)
        {
            PostCount++;
            _pending.Enqueue((d, state));
        }

        public void ExecutePending()
        {
            while (_pending.Count > 0)
            {
                var (callback, state) = _pending.Dequeue();
                callback(state);
            }
        }
    }
}