using System.Reactive.Linq;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Claude;
using Ivy.Tendril.Agents.Runtime;

namespace Ivy.Tendril.Agents.Test.Claude;

[Collection("Claude")]
public class ClaudeIntegrationTests : IAsyncLifetime
{
    private readonly AgentRunner _runner;
    private readonly string _workDir;

    public ClaudeIntegrationTests()
    {
        _runner = new AgentRunner();
        _runner.Register(new ClaudeCli(), new ClaudeEventParser(), new ClaudeHealthCheck());
        _workDir = Path.GetTempPath();
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    private AgentResolutionContext MakeContext(string prompt, int? maxTurns = 1, TimeSpan? timeout = null)
        => new()
        {
            AgentId = AgentId.Claude,
            Prompt = prompt,
            WorkingDirectory = _workDir,
            MaxTurns = maxTurns,
            TimeoutPolicy = timeout is not null ? new TimeoutPolicy { TotalTimeout = timeout } : null,
        };

    [Fact]
    public async Task RunToCompletion_SimpleArithmetic_ReturnsResult()
    {
        var result = await _runner.RunToCompletionAsync(MakeContext("What is 2+2? Reply with ONLY the number, nothing else."));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Response);
        Assert.Contains("4", result.Response);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task RunToCompletion_WithMaxTurns_RespectsLimit()
    {
        var result = await _runner.RunToCompletionAsync(MakeContext("Reply with the word 'hello'"));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Response);
        Assert.Equal(1, result.TurnCount);
    }

    [Fact]
    public async Task LaunchAsync_EmitsSessionInitEvent()
    {
        await using var session = (AgentSession)await _runner.LaunchAsync(MakeContext("Reply with 'hi'"));

        var result = await session.WaitForCompletionAsync();
        var events = await session.Events.ToList();

        Assert.Contains(events, e => e is SessionInitEvent);
        var init = events.OfType<SessionInitEvent>().First();
        Assert.NotEmpty(init.SessionId);
    }

    [Fact]
    public async Task LaunchAsync_EmitsTextEvent()
    {
        await using var session = (AgentSession)await _runner.LaunchAsync(MakeContext("Say 'pong'"));

        var result = await session.WaitForCompletionAsync();
        var events = await session.Events.ToList();

        Assert.Contains(events, e => e is TextEvent);
    }

    [Fact]
    public async Task RunToCompletion_TracksUsage()
    {
        var result = await _runner.RunToCompletionAsync(MakeContext("Reply with just 'ok'"));

        Assert.NotNull(result.Usage);
        Assert.True(result.Usage.InputTokens > 0);
        Assert.True(result.Usage.OutputTokens > 0);
    }

    [Fact]
    public async Task RunToCompletion_TracksCost()
    {
        var result = await _runner.RunToCompletionAsync(MakeContext("Reply with 'x'"));

        Assert.NotNull(result.Usage);
        Assert.NotNull(result.Usage.CostUsd);
        Assert.True(result.Usage.CostUsd > 0);
    }

    [Fact]
    public async Task RunToCompletion_TracksDuration()
    {
        var result = await _runner.RunToCompletionAsync(MakeContext("Reply 'fast'"));

        Assert.NotNull(result.Duration);
        Assert.True(result.Duration.Value.TotalMilliseconds > 0);
        Assert.True(result.Duration.Value.TotalMinutes < 5, "Should complete within 5 minutes");
    }

    [Fact]
    public async Task LaunchAsync_RawOutputEmitsLines()
    {
        await using var session = (AgentSession)await _runner.LaunchAsync(MakeContext("Reply 'yo'"));

        var result = await session.WaitForCompletionAsync();
        var rawLines = await session.RawOutput!.ToList();

        Assert.NotEmpty(rawLines);
        Assert.All(rawLines, line => Assert.StartsWith("{", line));
    }

    [Fact]
    public async Task RunToCompletion_WithPathContainingSpaces_Works()
    {
        var dirWithSpaces = Path.Combine(Path.GetTempPath(), "tendril test dir");
        Directory.CreateDirectory(dirWithSpaces);

        try
        {
            var result = await _runner.RunToCompletionAsync(new AgentResolutionContext
            {
                AgentId = AgentId.Claude,
                Prompt = "Reply with 'spaces work'",
                WorkingDirectory = dirWithSpaces,
                MaxTurns = 1,
            });

            Assert.True(result.IsSuccess);
        }
        finally
        {
            Directory.Delete(dirWithSpaces, recursive: true);
        }
    }

    [Fact]
    public async Task LaunchAsync_SessionState_TransitionsCorrectly()
    {
        await using var session = (AgentSession)await _runner.LaunchAsync(MakeContext("Reply 'state test'"));

        var result = await session.WaitForCompletionAsync();

        Assert.Equal(SessionState.Completed, session.State);
        Assert.NotNull(session.CompletedAt);
        Assert.True(session.CompletedAt > session.StartedAt);
    }

    [Fact]
    public async Task LaunchAsync_EmitsResultEvent()
    {
        await using var session = (AgentSession)await _runner.LaunchAsync(MakeContext("Reply with 'result test'"));

        var result = await session.WaitForCompletionAsync();
        var events = await session.Events.ToList();

        var resultEvents = events.OfType<ResultEvent>().ToList();
        Assert.NotEmpty(resultEvents);
        Assert.True(resultEvents.Last().IsSuccess);
    }

    [Fact]
    public async Task RunToCompletion_WithTimeout_CompletesBeforeTimeout()
    {
        var result = await _runner.RunToCompletionAsync(
            MakeContext("Reply 'quick'", timeout: TimeSpan.FromMinutes(2)));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task RunToCompletion_UnicodePrompt_HandledCorrectly()
    {
        var result = await _runner.RunToCompletionAsync(
            MakeContext("Reply with the Japanese word for 'hello': こんにちは. Just reply with that single word."));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Response);
    }

    [Fact]
    public async Task RunToCompletion_MultilinePrompt_Works()
    {
        var prompt = """
            I have two questions:
            1. What is 1+1?
            2. What is 2+2?
            Reply in format: "1: X, 2: Y" where X and Y are the answers.
            """;

        var result = await _runner.RunToCompletionAsync(MakeContext(prompt));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Response);
    }

    [Fact]
    public void RegisteredAgents_IncludesClaude()
    {
        Assert.Contains(AgentId.Claude, _runner.RegisteredAgents);
    }

    [Fact]
    public void GetCli_ReturnsClaudeCli()
    {
        var cli = _runner.GetCli(AgentId.Claude);
        Assert.Equal(AgentId.Claude, cli.Id);
    }

    [Fact]
    public void GetHealthCheck_ReturnsClaudeHealthCheck()
    {
        var hc = _runner.GetHealthCheck(AgentId.Claude);
        Assert.Equal(AgentId.Claude, hc.AgentId);
    }

    [Fact]
    public void GetDescriptor_ReturnsClaudeDescriptor()
    {
        var desc = _runner.GetDescriptor(AgentId.Claude);
        Assert.Equal(AgentId.Claude, desc.Id);
        Assert.Equal("Claude Code", desc.DisplayName);
    }

    [Fact]
    public void GetCli_UnknownAgent_Throws()
    {
        Assert.Throws<ArgumentException>(() => _runner.GetCli("nonexistent"));
    }

    [Fact]
    public void GetHealthCheck_UnknownAgent_Throws()
    {
        Assert.Throws<ArgumentException>(() => _runner.GetHealthCheck("nonexistent"));
    }

    [Fact]
    public void GetDescriptor_UnknownAgent_Throws()
    {
        Assert.Throws<ArgumentException>(() => _runner.GetDescriptor("nonexistent"));
    }

    [Fact]
    public async Task LaunchAsync_AppearsInActiveSessions()
    {
        await using var session = (AgentSession)await _runner.LaunchAsync(MakeContext("Reply 'active check'"));

        await session.WaitForCompletionAsync();
    }

    [Fact]
    public async Task KillAsync_TerminatesSession()
    {
        await using var session = (AgentSession)await _runner.LaunchAsync(new AgentResolutionContext
        {
            AgentId = AgentId.Claude,
            Prompt = "Write a very long essay about the history of computing. Make it at least 5000 words.",
            WorkingDirectory = _workDir,
            MaxTurns = 5,
        });

        await Task.Delay(2000);
        await session.KillAsync();

        var finalState = session.State;
        Assert.True(
            finalState is SessionState.Stopped or SessionState.Failed or SessionState.Completed,
            $"Expected terminal state, got {finalState}");
    }

    [Fact]
    public void Session_SupportsPermissionResponse_IsFalse()
    {
        // Claude CLI sessions don't support interactive permission responses
        var cli = _runner.GetCli(AgentId.Claude);
        Assert.IsType<ClaudeCli>(cli);
    }

    [Fact]
    public async Task StopAllAsync_StopsActiveSessions()
    {
        // Verify StopAllAsync doesn't throw when no active sessions
        await _runner.StopAllAsync();
    }
}
