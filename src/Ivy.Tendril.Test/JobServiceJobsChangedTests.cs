using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

public class JobServiceJobsChangedTests
{
    private static JobService CreateService()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        return new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void CompleteJob_RaisesJobsChangedEvent()
    {
        var service = CreateService();
        var id = service.CreateTestJob(new ExecutePlanArgs("test-plan"));

        var fired = false;
        service.JobsChanged += () => fired = true;
        service.CompleteJob(id, 0);

        Assert.True(fired);
    }

    [Fact]
    public void CompleteJob_Failure_RaisesJobsChangedEvent()
    {
        var service = CreateService();
        var id = service.CreateTestJob(new ExecutePlanArgs("test-plan"));

        var fired = false;
        service.JobsChanged += () => fired = true;
        service.CompleteJob(id, 1);

        Assert.True(fired);
    }

    [Fact]
    public void StopJob_RaisesJobsChangedEvent()
    {
        var service = CreateService();
        var id = service.CreateTestJob(new ExecutePlanArgs("test-plan"));

        var fired = false;
        service.JobsChanged += () => fired = true;
        service.StopJob(id);

        Assert.True(fired);
    }

    [Fact]
    public void CreateTestJob_WithChatSessionId_TracksSpawnedJob()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TendrilChatTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var config = new ConfigService(new TendrilSettings(), tempDir);
            var chatHistory = new ChatHistoryService(config);
            var session = chatHistory.CreateSession("claude", "opus");

            var service = new JobService(config, chatHistoryService: chatHistory);
            var args = new ExecutePlanArgs("test-plan") { ChatSessionId = session.Id };
            var id = service.CreateTestJob(args);

            var job = service.GetJob(id);
            Assert.NotNull(job);
            Assert.Equal(session.Id, job.ChatSessionId);

            var spawned = chatHistory.GetSpawnedJobs(session.Id);
            Assert.Contains(id, spawned);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
