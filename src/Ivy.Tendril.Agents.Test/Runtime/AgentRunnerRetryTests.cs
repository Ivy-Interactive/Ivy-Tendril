using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Claude;
using Ivy.Tendril.Agents.Runtime;

namespace Ivy.Tendril.Agents.Test.Runtime;

[Collection("Claude")]
public class AgentRunnerRetryTests
{
    private readonly AgentRunner _runner;
    private readonly string _workDir;

    public AgentRunnerRetryTests()
    {
        _runner = new AgentRunner();
        _runner.Register(
            new ClaudeCli(),
            new ClaudeEventParser(),
            new ClaudeHealthCheck(),
            new ClaudeFailureAnalyzer(),
            new ClaudeSessionCostParser(),
            new ClaudePty());
        _workDir = Path.GetTempPath();
    }

    [Fact]
    public async Task RunToCompletion_WithRetryPolicy_SuccessDoesNotRetry()
    {
        var policy = new CountingRetryPolicy();
        var context = new AgentResolutionContext
        {
            AgentId = AgentId.Claude,
            Prompt = "Reply with ONLY the word 'hello'",
            WorkingDirectory = _workDir,
            MaxTurns = 1,
            RetryPolicy = policy,
        };

        var result = await _runner.RunToCompletionAsync(context);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, policy.CallCount);
    }

    [Fact]
    public async Task RunToCompletion_WithoutRetryPolicy_ValidationFailureThrows()
    {
        var context = new AgentResolutionContext
        {
            AgentId = AgentId.Claude,
            Prompt = "Reply with 'hi'",
            WorkingDirectory = @"C:\NonExistentPath_12345",
            MaxTurns = 1,
        };

        await Assert.ThrowsAsync<AgentLaunchException>(
            () => _runner.RunToCompletionAsync(context));
    }

    [Fact]
    public async Task RunToCompletion_WithRecordingBasePath_CreatesRecordingFile()
    {
        var recordingDir = Path.Combine(Path.GetTempPath(), $"tendril-test-{Guid.NewGuid():N}");

        try
        {
            var context = new AgentResolutionContext
            {
                AgentId = AgentId.Claude,
                Prompt = "Reply with ONLY the word 'recorded'",
                WorkingDirectory = _workDir,
                MaxTurns = 1,
                RecordingBasePath = recordingDir,
            };

            var result = await _runner.RunToCompletionAsync(context);

            Assert.True(result.IsSuccess);
            Assert.True(Directory.Exists(recordingDir));
            var files = Directory.GetFiles(recordingDir, "*.jsonl");
            Assert.Single(files);
            var content = await File.ReadAllTextAsync(files[0]);
            Assert.NotEmpty(content);
        }
        finally
        {
            if (Directory.Exists(recordingDir))
                Directory.Delete(recordingDir, recursive: true);
        }
    }

    [Fact]
    public async Task LaunchAsync_WithInteractionHandler_SetsSupportsPermission()
    {
        var context = new AgentResolutionContext
        {
            AgentId = AgentId.Claude,
            Prompt = "Reply with ONLY the word 'test'",
            WorkingDirectory = _workDir,
            MaxTurns = 1,
            InteractionHandler = AutoApproveHandler.Instance,
        };

        await using var session = (AgentSession)await _runner.LaunchAsync(context);

        Assert.True(session.SupportsPermissionResponse);
        Assert.True(session.SupportsQuestionResponse);

        await session.WaitForCompletionAsync();
    }

    [Fact]
    public async Task LaunchAsync_WithoutInteractionHandler_SupportsPermissionIsFalse()
    {
        var context = new AgentResolutionContext
        {
            AgentId = AgentId.Claude,
            Prompt = "Reply with ONLY the word 'test'",
            WorkingDirectory = _workDir,
            MaxTurns = 1,
        };

        await using var session = (AgentSession)await _runner.LaunchAsync(context);

        Assert.False(session.SupportsPermissionResponse);
        Assert.False(session.SupportsQuestionResponse);

        await session.WaitForCompletionAsync();
    }

    [Fact]
    public async Task LaunchAsync_WithIdleTimeout_SessionCompletesBeforeTimeout()
    {
        var context = new AgentResolutionContext
        {
            AgentId = AgentId.Claude,
            Prompt = "Reply with ONLY the word 'quick'",
            WorkingDirectory = _workDir,
            MaxTurns = 1,
            TimeoutPolicy = new TimeoutPolicy
            {
                IdleTimeout = TimeSpan.FromMinutes(5),
                TotalTimeout = TimeSpan.FromMinutes(1),
            },
        };

        await using var session = (AgentSession)await _runner.LaunchAsync(context);
        var result = await session.WaitForCompletionAsync();

        Assert.True(result.IsSuccess);
        Assert.False(session.IdleTimeoutFired);
    }

    private sealed class CountingRetryPolicy : IRetryPolicy
    {
        public int CallCount { get; private set; }

        public RetryDecision ShouldRetry(RetryContext context)
        {
            CallCount++;
            return RetryDecision.No;
        }
    }
}
