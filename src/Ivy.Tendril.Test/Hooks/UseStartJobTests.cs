using Ivy.Core.Exceptions;
using Ivy.Core.Hooks;
using Ivy.Tendril.Hooks;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Ivy.Tendril.Test.Hooks;

public class UseStartJobTests
{
    private static ViewContext CreateViewContext(TestJobService jobService)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExceptionHandler>(new StubExceptionHandler());
        services.AddSingleton<IJobService>(jobService);
        var provider = services.BuildServiceProvider();
        return new ViewContext(() => { }, null, provider);
    }

    [Fact]
    public void UseStartJob_IsStarting_StartsAsFalse()
    {
        // Arrange
        var jobService = new TestJobService();
        var ctx = CreateViewContext(jobService);

        // Act
        var (_, isStarting) = ctx.UseStartJob();

        // Assert
        Assert.False(isStarting);
    }

    [Fact]
    public void UseStartJob_CallsUnderlyingJobService()
    {
        // Arrange
        var jobService = new TestJobService();
        var ctx = CreateViewContext(jobService);

        // Act
        var (startJob, _) = ctx.UseStartJob();
        startJob("CreatePlan", new CreatePlanArgs("Test Plan", "TestProject"));

        // Assert
        Assert.Single(jobService.StartedJobs);
        var (type, args) = jobService.StartedJobs[0];
        Assert.Equal("CreatePlan", type);
        var createPlanArgs = Assert.IsType<CreatePlanArgs>(args);
        Assert.Equal("Test Plan", createPlanArgs.Description);
        Assert.Equal("TestProject", createPlanArgs.Project);
    }

    [Fact]
    public void UseStartJob_SubsequentCalls_IgnoredWhenIsStarting()
    {
        // Arrange
        var jobService = new TestJobService();
        var ctx = CreateViewContext(jobService);
        var (startJob, _) = ctx.UseStartJob();

        // Act
        startJob("TestJob1", new CreatePlanArgs("task1", "Auto"));
        startJob("TestJob2", new CreatePlanArgs("task2", "Auto"));
        startJob("TestJob3", new CreatePlanArgs("task3", "Auto"));

        // Assert - only the first call should have triggered StartJob
        Assert.Single(jobService.StartedJobs);
        Assert.Equal("TestJob1", jobService.StartedJobs[0].Type);
    }

    [Fact]
    public void UseStartJob_IsStarting_BecomesTrueAfterStartJob()
    {
        // Arrange
        var jobService = new TestJobService();
        var ctx = CreateViewContext(jobService);

        // Act - First render
        var (startJob, isStartingBefore) = ctx.UseStartJob();
        Assert.False(isStartingBefore);

        startJob("TestJob", new CreatePlanArgs("task", "Auto"));

        // Second render - reset context and call hook again
        ctx.Reset();
        var (_, isStartingAfter) = ctx.UseStartJob();

        // Assert
        Assert.True(isStartingAfter);
    }

    private class TestJobService : IJobService
    {
        public List<(string Type, JobArgsBase Args)> StartedJobs { get; } = new();

        public string StartJob(string type, JobArgsBase args, string? inboxFilePath = null)
        {
            StartedJobs.Add((type, args));
            return Guid.NewGuid().ToString();
        }

        public void CompleteJob(string id, int? exitCode, bool timedOut = false, bool staleOutput = false)
        {
        }

        public void StopJob(string id)
        {
        }

        public void DeleteJob(string id)
        {
        }

        public void ClearCompletedJobs()
        {
        }

        public void ClearFailedJobs()
        {
        }

        public JobItem? GetJob(string id)
        {
            return null;
        }

        public List<JobItem> GetJobs()
        {
            return new List<JobItem>();
        }

        public bool IsInboxFileTracked(string filePath)
        {
            return false;
        }

        public void Dispose()
        {
        }

        public List<JobItem> GetRecentlyCompletedJobs(int count = 10)
        {
            return new List<JobItem>();
        }
#pragma warning disable CS0067
        public event Action? JobsChanged;
        public event Action? JobsStructureChanged;
        public event Action? JobPropertyChanged;
        public event Action<JobNotification>? NotificationReady;
#pragma warning restore CS0067
    }

    private class StubExceptionHandler : IExceptionHandler
    {
        public bool HandleException(Exception exception)
        {
            return false;
        }
    }
}