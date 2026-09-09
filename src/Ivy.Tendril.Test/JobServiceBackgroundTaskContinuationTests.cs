using System.Diagnostics;
using Ivy.Helpers;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Jobs;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test;

public class JobServiceBackgroundTaskContinuationTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    private static JobService CreateService(TimeSpan jobTimeout, TimeSpan staleOutputTimeout)
    {
        SynchronizationContext.SetSynchronizationContext(null);
        return new JobService(jobTimeout, staleOutputTimeout);
    }

    private sealed class StubJobLauncher : JobLauncher
    {
        public bool RelaunchCalled { get; private set; }
        public JobItem? LastJob { get; private set; }
        public string? LastPrompt { get; private set; }
        public bool ReturnValue { get; set; } = true;

        public StubJobLauncher() : base(null, null, NullLogger.Instance, "")
        {
        }

        public override bool RelaunchAsContinuation(JobItem job, string continuationPrompt)
        {
            RelaunchCalled = true;
            LastJob = job;
            LastPrompt = continuationPrompt;
            return ReturnValue;
        }
    }

    private sealed class RecordingAgentCli : IAgentCli
    {
        public string Id => "claude";
        public string DisplayName => "Claude";
        public AgentCapabilities Capabilities => AgentCapabilities.SessionResume;
        public TransportKind SupportedTransports => TransportKind.CliSpawn;
        public IReadOnlyList<AgentProfileDefault> DefaultProfiles => [];
        public string? ContextFileName => null;
        public PromptTransport PromptTransport => PromptTransport.Stdin;
        public OutputFormat PreferredOutputFormat => OutputFormat.StreamJson;
        public string? TranslateToolName(string canonicalTool) => canonicalTool;
        public string? ReverseTranslateToolName(string nativeTool) => nativeTool;
        public IReadOnlyList<string> ExtractWritableDirectories(IReadOnlyList<string> allowedTools) => [];
        public IReadOnlyDictionary<string, string> GetDefaultEnvironment() => new Dictionary<string, string>();

        public AgentLaunchConfig? LastConfig { get; private set; }

        public AgentProcessSpec BuildProcessSpec(AgentLaunchConfig config)
        {
            LastConfig = config;
            return new AgentProcessSpec
            {
                FileName = "dummy",
                Arguments = [],
                WorkingDirectory = config.WorkingDirectory,
                Environment = new Dictionary<string, string>()
            };
        }
    }

    private sealed class StubAgentRunner : IAgentRunner
    {
        public RecordingAgentCli Cli { get; } = new();

        public Task<IAgentSession> LaunchAsync(AgentResolutionContext context, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ResultEvent> RunToCompletionAsync(AgentResolutionContext context, CancellationToken ct = default) => throw new NotImplementedException();
        public IReadOnlyList<IAgentSession> ActiveSessions => [];
        public IObservable<IAgentSession> Sessions => throw new NotImplementedException();
        public Task StopAllAsync(CancellationToken ct = default) => Task.CompletedTask;
        public IReadOnlyList<string> RegisteredAgents => ["claude"];
        public IAgentCli GetCli(string agentId) => Cli;
        public IEventParser GetParser(string agentId) => throw new NotImplementedException();
        public IAgentHealthCheck GetHealthCheck(string agentId) => throw new NotImplementedException();
        public IAgentDescriptor GetDescriptor(string agentId) => throw new NotImplementedException();
        public IFailureAnalyzer? GetFailureAnalyzer(string agentId) => null;
        public ISessionCostParser? GetCostParser(string agentId) => null;
        public IAgentPty? GetPty(string agentId) => null;
        public IModelCatalogProvider? GetModelCatalog(string agentId) => null;
        public IEnumerable<IModelCatalogProvider> ModelCatalogs => [];
    }

    [Fact]
    public void CompleteJob_Exit0_WithBackgroundTask_BelowCap_RelaunchesAndDoesNotComplete()
    {
        var service = CreateService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10));
        var stubLauncher = new StubJobLauncher();
        service.JobLauncher = stubLauncher;

        var id = service.CreateTestJob(new ExecutePlanArgs(_tempDir.Path));
        var job = service.GetJob(id)!;
        job.SessionId = "test-session-123";
        job.OutputLines.Enqueue("""{"kind":"tool_result","tool_use_id":"t1","output":"Command did not complete within its 120s timeout and was moved to the background (ID: b28ur1i7n)."}""");

        service.CompleteJob(id, 0);

        Assert.Equal(JobStatus.Running, job.Status);
        Assert.Null(job.CompletedAt);
        Assert.Null(job.StatusMessage);
        Assert.Equal(1, job.BackgroundContinuationCount);
        Assert.True(stubLauncher.RelaunchCalled);
        Assert.Equal("test-session-123", stubLauncher.LastJob?.SessionId);
        Assert.NotNull(stubLauncher.LastPrompt);
        Assert.Contains("b28ur1i7n", stubLauncher.LastPrompt);
    }

    [Fact]
    public void CompleteJob_Exit0_WithBackgroundTask_AtCap_CompletesNormally()
    {
        var service = CreateService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10));
        var stubLauncher = new StubJobLauncher();
        service.JobLauncher = stubLauncher;

        var id = service.CreateTestJob(new ExecutePlanArgs(_tempDir.Path));
        var job = service.GetJob(id)!;
        job.BackgroundContinuationCount = JobService.MaxBackgroundContinuations;
        job.OutputLines.Enqueue("""{"kind":"tool_result","tool_use_id":"t1","output":"Command did not complete within its 120s timeout and was moved to the background (ID: b28ur1i7n)."}""");

        service.CompleteJob(id, 0);

        Assert.Equal(JobStatus.Completed, job.Status);
        Assert.NotNull(job.CompletedAt);
        Assert.False(stubLauncher.RelaunchCalled);
    }

    [Fact]
    public void CompleteJob_Exit0_NoBackgroundTask_CompletesNormally()
    {
        var service = CreateService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10));
        var stubLauncher = new StubJobLauncher();
        service.JobLauncher = stubLauncher;

        var id = service.CreateTestJob(new ExecutePlanArgs(_tempDir.Path));
        var job = service.GetJob(id)!;
        job.OutputLines.Enqueue("""{"kind":"tool_result","tool_use_id":"t1","output":"Files copied successfully."}""");

        service.CompleteJob(id, 0);

        Assert.Equal(JobStatus.Completed, job.Status);
        Assert.NotNull(job.CompletedAt);
        Assert.False(stubLauncher.RelaunchCalled);
        Assert.Equal(0, job.BackgroundContinuationCount);
    }

    [Fact]
    public void CompleteJob_NonZeroExit_WithBackgroundTask_FailsWithoutContinuation()
    {
        var service = CreateService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10));
        var stubLauncher = new StubJobLauncher();
        service.JobLauncher = stubLauncher;

        var id = service.CreateTestJob(new ExecutePlanArgs(_tempDir.Path));
        var job = service.GetJob(id)!;
        job.OutputLines.Enqueue("""{"kind":"tool_result","tool_use_id":"t1","output":"Command running in background with ID: b1hduuleo"}""");

        service.CompleteJob(id, 1);

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.NotNull(job.CompletedAt);
        Assert.False(stubLauncher.RelaunchCalled);
        Assert.Equal(0, job.BackgroundContinuationCount);
    }

    [Fact]
    public void CompleteJob_TimedOut_WithBackgroundTask_TimesOutWithoutContinuation()
    {
        var service = CreateService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10));
        var stubLauncher = new StubJobLauncher();
        service.JobLauncher = stubLauncher;

        var id = service.CreateTestJob(new ExecutePlanArgs(_tempDir.Path));
        var job = service.GetJob(id)!;
        job.OutputLines.Enqueue("""{"kind":"tool_result","tool_use_id":"t1","output":"Command running in background with ID: b1hduuleo"}""");

        service.CompleteJob(id, 0, timedOut: true);

        Assert.Equal(JobStatus.Timeout, job.Status);
        Assert.NotNull(job.CompletedAt);
        Assert.False(stubLauncher.RelaunchCalled);
        Assert.Equal(0, job.BackgroundContinuationCount);
    }

    [Fact]
    public void JobLauncher_RelaunchAsContinuation_BuildsConfigWithResumeAndSameSessionId()
    {
        var agentRunner = new StubAgentRunner();
        var launcher = new JobLauncher(null, agentRunner, NullLogger.Instance, "");

        var job = new JobItem
        {
            Id = "test-job-42",
            Type = "ExecutePlan",
            Project = "test-project",
            SessionId = "session-resume-abc",
            WorkingDirectory = _tempDir.Path,
            Status = JobStatus.Running,
        };

        var jobs = new System.Collections.Concurrent.ConcurrentDictionary<string, JobItem>();
        jobs[job.Id] = job;
        var ctx = new JobLaunchContext(
            job,
            jobs,
            new SemaphoreSlim(1, 1),
            () => TimeSpan.FromMinutes(30),
            () => TimeSpan.FromMinutes(10),
            (_, _, _, _, _) => { },
            (_, _, _, _) => { },
            () => { });

        var prompt = "Continuation prompt text";
        // RelaunchAsContinuation will resolve agent, build launch config with Resume = true,
        // build process spec with RecordingAgentCli, and attempt to start the process.
        // StartAgentProcess will fail or succeed depending on process start, but BuildProcessSpec is invoked.
        launcher.RelaunchAsContinuation(ctx, prompt);

        var config = agentRunner.Cli.LastConfig;
        Assert.NotNull(config);
        Assert.True(config.Resume);
        Assert.Equal("session-resume-abc", config.SessionId);
        Assert.Equal(prompt, config.Prompt);
    }
}
