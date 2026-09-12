using System.Text;
using Ivy.Tendril.Controllers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Jobs;
using Ivy.Tendril.Test.TestHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Ivy.Tendril.Test;

public class JobControllerTests
{
    [Fact]
    public async Task GetJobEvents_ReplaysExistingOutputLines()
    {
        var job = new JobItem
        {
            Id = "00001",
            Status = JobStatus.Completed
        };
        job.OutputLines.Enqueue("{\"kind\":\"text\",\"text\":\"hello\"}");
        job.OutputLines.Enqueue("{\"kind\":\"text\",\"text\":\"world\"}");

        var jobService = new StubJobService();
        jobService.Jobs[job.Id] = job;

        var controller = CreateController(jobService);
        var bodyStream = new MemoryStream();
        controller.ControllerContext.HttpContext.Response.Body = bodyStream;

        await controller.GetJobEvents("00001", CancellationToken.None);

        Assert.Equal("text/event-stream", controller.Response.ContentType);
        Assert.Equal("no-cache", controller.Response.Headers["Cache-Control"].ToString());

        bodyStream.Position = 0;
        using var reader = new StreamReader(bodyStream, Encoding.UTF8);
        var content = await reader.ReadToEndAsync();

        Assert.Contains("data: {\"kind\":\"text\",\"text\":\"hello\"}\n\n", content);
        Assert.Contains("data: {\"kind\":\"text\",\"text\":\"world\"}\n\n", content);
        Assert.Contains("event: end\ndata: {\"status\":\"Completed\"}\n\n", content);
    }

    [Fact]
    public async Task GetJobEvents_StreamsLiveEvents()
    {
        var job = new JobItem
        {
            Id = "00002",
            Status = JobStatus.Running
        };

        var jobService = new StubJobService();
        jobService.Jobs[job.Id] = job;

        var controller = CreateController(jobService);
        var bodyStream = new MemoryStream();
        controller.ControllerContext.HttpContext.Response.Body = bodyStream;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var streamTask = controller.GetJobEvents("00002", cts.Token);

        // Push live output
        job.EnqueueSystemOutput("live message from agent");

        // Complete the job
        job.Status = JobStatus.Completed;
        job.DisposeResources();
        jobService.NotifyJobFinished(job);

        await streamTask;

        bodyStream.Position = 0;
        using var reader = new StreamReader(bodyStream, Encoding.UTF8);
        var content = await reader.ReadToEndAsync();

        Assert.Contains("live message from agent", content);
        Assert.Contains("event: end\ndata: {\"status\":\"Completed\"}\n\n", content);
    }

    [Fact]
    public async Task GetJobEvents_ReturnsNotFoundForUnknownJob()
    {
        var jobService = new StubJobService();
        var controller = CreateController(jobService);
        var bodyStream = new MemoryStream();
        controller.ControllerContext.HttpContext.Response.Body = bodyStream;

        await controller.GetJobEvents("nonexistent-999", CancellationToken.None);

        Assert.Equal(404, controller.Response.StatusCode);
    }

    [Fact]
    public async Task GetJobEvents_FiltersBufferedEventsByKind()
    {
        var job = new JobItem
        {
            Id = "00003",
            Status = JobStatus.Completed
        };
        job.OutputLines.Enqueue("{\"kind\":\"text\",\"text\":\"hello\"}");
        job.OutputLines.Enqueue("{\"kind\":\"tool_call\",\"tool_name\":\"read_file\"}");
        job.OutputLines.Enqueue("{\"kind\":\"thinking\",\"content\":\"thinking...\"}");

        var jobService = new StubJobService();
        jobService.Jobs[job.Id] = job;

        var controller = CreateController(jobService);
        var bodyStream = new MemoryStream();
        controller.ControllerContext.HttpContext.Response.Body = bodyStream;

        await controller.GetJobEvents("00003", ["tool_call"], CancellationToken.None);

        bodyStream.Position = 0;
        using var reader = new StreamReader(bodyStream, Encoding.UTF8);
        var content = await reader.ReadToEndAsync();

        Assert.Contains("data: {\"kind\":\"tool_call\",\"tool_name\":\"read_file\"}\n\n", content);
        Assert.DoesNotContain("hello", content);
        Assert.DoesNotContain("thinking...", content);
        Assert.Contains("event: end\ndata: {\"status\":\"Completed\"}\n\n", content);
    }

    [Fact]
    public async Task GetJobEvents_FiltersLiveEventsByKind()
    {
        var job = new JobItem
        {
            Id = "00004",
            Status = JobStatus.Running
        };

        var jobService = new StubJobService();
        jobService.Jobs[job.Id] = job;

        var controller = CreateController(jobService);
        var bodyStream = new MemoryStream();
        controller.ControllerContext.HttpContext.Response.Body = bodyStream;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var streamTask = controller.GetJobEvents("00004", ["tool_call"], cts.Token);

        job.EnqueueOutput("{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"ignored live text\"}]}}");
        job.EnqueueOutput("{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"tool_use\",\"id\":\"call_1\",\"name\":\"execute_cmd\",\"input\":{}}]}}");

        job.Status = JobStatus.Completed;
        job.DisposeResources();
        jobService.NotifyJobFinished(job);

        await streamTask;

        bodyStream.Position = 0;
        using var reader = new StreamReader(bodyStream, Encoding.UTF8);
        var content = await reader.ReadToEndAsync();

        Assert.Contains("data: {\"kind\":\"tool_call\",", content);
        Assert.Contains("execute_cmd", content);
        Assert.DoesNotContain("ignored live text", content);
        Assert.Contains("event: end\ndata: {\"status\":\"Completed\"}\n\n", content);
    }

    [Fact]
    public async Task GetJobEvents_SupportsCommaSeparatedKinds()
    {
        var job = new JobItem
        {
            Id = "00005",
            Status = JobStatus.Completed
        };
        job.OutputLines.Enqueue("{\"kind\":\"text\",\"text\":\"message\"}");
        job.OutputLines.Enqueue("{\"kind\":\"error\",\"message\":\"bad request\"}");
        job.OutputLines.Enqueue("{\"kind\":\"tool_call\",\"tool_name\":\"grep\"}");

        var jobService = new StubJobService();
        jobService.Jobs[job.Id] = job;

        var controller = CreateController(jobService);
        var bodyStream = new MemoryStream();
        controller.ControllerContext.HttpContext.Response.Body = bodyStream;

        await controller.GetJobEvents("00005", ["text,error"], CancellationToken.None);

        bodyStream.Position = 0;
        using var reader = new StreamReader(bodyStream, Encoding.UTF8);
        var content = await reader.ReadToEndAsync();

        Assert.Contains("data: {\"kind\":\"text\",\"text\":\"message\"}\n\n", content);
        Assert.Contains("data: {\"kind\":\"error\",\"message\":\"bad request\"}\n\n", content);
        Assert.DoesNotContain("grep", content);
        Assert.Contains("event: end\ndata: {\"status\":\"Completed\"}\n\n", content);
    }

    [Fact]
    public async Task GetJobEvents_ExpandsToolUseAlias()
    {
        var job = new JobItem
        {
            Id = "00006",
            Status = JobStatus.Completed
        };
        job.OutputLines.Enqueue("{\"kind\":\"text\",\"text\":\"hello\"}");
        job.OutputLines.Enqueue("{\"kind\":\"tool_call\",\"tool_name\":\"execute\"}");
        job.OutputLines.Enqueue("{\"kind\":\"tool_result\",\"output\":\"success\"}");

        var jobService = new StubJobService();
        jobService.Jobs[job.Id] = job;

        var controller = CreateController(jobService);
        var bodyStream = new MemoryStream();
        controller.ControllerContext.HttpContext.Response.Body = bodyStream;

        await controller.GetJobEvents("00006", ["tool_use"], CancellationToken.None);

        bodyStream.Position = 0;
        using var reader = new StreamReader(bodyStream, Encoding.UTF8);
        var content = await reader.ReadToEndAsync();

        Assert.Contains("data: {\"kind\":\"tool_call\",\"tool_name\":\"execute\"}\n\n", content);
        Assert.Contains("data: {\"kind\":\"tool_result\",\"output\":\"success\"}\n\n", content);
        Assert.DoesNotContain("hello", content);
        Assert.Contains("event: end\ndata: {\"status\":\"Completed\"}\n\n", content);
    }

    [Fact]
    public async Task GetJobEvents_AlwaysEmitsEndEventRegardlessOfFilter()
    {
        var job = new JobItem
        {
            Id = "00007",
            Status = JobStatus.Completed
        };
        job.OutputLines.Enqueue("{\"kind\":\"text\",\"text\":\"hello\"}");

        var jobService = new StubJobService();
        jobService.Jobs[job.Id] = job;

        var controller = CreateController(jobService);
        var bodyStream = new MemoryStream();
        controller.ControllerContext.HttpContext.Response.Body = bodyStream;

        await controller.GetJobEvents("00007", ["tool_call"], CancellationToken.None);

        bodyStream.Position = 0;
        using var reader = new StreamReader(bodyStream, Encoding.UTF8);
        var content = await reader.ReadToEndAsync();

        Assert.DoesNotContain("hello", content);
        Assert.Contains("event: end\ndata: {\"status\":\"Completed\"}\n\n", content);
    }

    private static JobController CreateController(IJobService jobService)
    {
        var controller = new JobController(jobService, new StubConfigService());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    private class StubJobService : IJobService
    {
        public Dictionary<string, JobItem> Jobs { get; } = new();

        public event Action<JobItem>? JobFinished;

        public void NotifyJobFinished(JobItem job)
        {
            JobFinished?.Invoke(job);
        }

        public string StartJob(JobArgsBase args, string? inboxFilePath = null)
        {
            var id = $"job-{Jobs.Count + 1}";
            Jobs[id] = new JobItem { Id = id };
            return id;
        }

        public void ForceStartJob(string id) { }
        public void CompleteJob(string id, int? exitCode, bool timedOut = false, bool staleOutput = false) { }
        public void StopJob(string id) { }
        public int StopAllJobs() => 0;
        public void DeleteJob(string id) { }
        public void ClearCompletedJobs() { }
        public void ClearFailedJobs() { }
        public void ClearAllJobs() { }
        public int StopQueuedJobs() => 0;
        public List<JobItem> GetJobs() => Jobs.Values.ToList();
        public List<JobItem> GetJobsForPlan(string planFile) => [];
        public JobItem? GetJob(string id) => Jobs.GetValueOrDefault(id);
        public bool UpdateJobStatus(string id, string message, string? planId = null, string? planTitle = null) => false;
        public void SetChatSessionId(string id, string chatSessionId) { }
        public bool ReportJobFailure(string id, string message) => false;
        public bool IsInboxFileTracked(string filePath) => false;
        public void Dispose() { }

#pragma warning disable CS0067
        public event Action? JobsChanged;
        public event Action? JobsStructureChanged;
        public event Action? JobPropertyChanged;
        public event Action<JobNotification>? NotificationReady;
#pragma warning restore CS0067
    }
}
