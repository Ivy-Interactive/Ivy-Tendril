using Ivy.Tendril.Controllers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

// ReSharper disable UnusedAutoPropertyAccessor.Local

namespace Ivy.Tendril.Test;

public class InboxControllerTests
{
    [Fact]
    public void PostPlan_ValidRequest_ReturnsOkWithJobId()
    {
        var jobService = new StubJobService();
        var controller = CreateController(jobService);

        var result = controller.PostPlan(new CreatePlanRequest("Fix a bug", "Tendril"));

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);
        Assert.Single(jobService.StartedJobs);
        Assert.Equal("CreatePlan", jobService.StartedJobs[0].Type);
    }

    [Fact]
    public void PostPlan_EmptyDescription_ReturnsBadRequest()
    {
        var jobService = new StubJobService();
        var controller = CreateController(jobService);

        var result = controller.PostPlan(new CreatePlanRequest(""));

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(jobService.StartedJobs);
    }

    [Fact]
    public void PostPlan_WithSourcePath_PassesItToJobService()
    {
        var jobService = new StubJobService();
        var controller = CreateController(jobService);

        var result = controller.PostPlan(new CreatePlanRequest("Fix bug", "Tendril", @"D:\Tests\Session1"));

        Assert.IsType<OkObjectResult>(result);
        var job = Assert.Single(jobService.StartedJobs);
        var cpArgs = Assert.IsType<CreatePlanArgs>(job);
        Assert.Equal(@"D:\Tests\Session1", cpArgs.SourcePath);
    }

    [Fact]
    public void PostPlan_NullProject_DefaultsToAuto()
    {
        var jobService = new StubJobService();
        var controller = CreateController(jobService);

        var result = controller.PostPlan(new CreatePlanRequest("Some task"));

        Assert.IsType<OkObjectResult>(result);
        var job = Assert.Single(jobService.StartedJobs);
        var cpArgs = Assert.IsType<CreatePlanArgs>(job);
        Assert.Equal("Auto", cpArgs.Project);
    }

    private static InboxController CreateController(IJobService jobService)
    {
        var controller = new InboxController(jobService);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    private class StubJobService : IJobService
    {
        public List<JobArgsBase> StartedJobs { get; } = new();

        public string StartJob(JobArgsBase args, string? inboxFilePath = null)
        {
            var id = $"job-{StartedJobs.Count + 1}";
            StartedJobs.Add(args);
            return id;
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

        public List<JobItem> GetJobs()
        {
            return new List<JobItem>();
        }

        public JobItem? GetJob(string id)
        {
            return null;
        }

        public bool IsInboxFileTracked(string filePath)
        {
            return false;
        }

        public void Dispose()
        {
        }

#pragma warning disable CS0067
        public event Action? JobsChanged;
        public event Action? JobsStructureChanged;
        public event Action? JobPropertyChanged;
        public event Action<JobNotification>? NotificationReady;
#pragma warning restore CS0067
    }
}