using System.Reactive.Linq;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Runtime;

namespace Ivy.Tendril.Agents.Test.Runtime;

public class TestSessionBuilderTests
{
    [Fact]
    public void Build_MinimalBuilder_ReturnsCompletedSession()
    {
        var session = new TestSessionBuilder().Build();

        Assert.Equal(SessionState.Completed, session.State);
        Assert.NotNull(session.Result);
        Assert.True(session.Result.IsSuccess);
    }

    [Fact]
    public void Build_WithAgentId_SetsAgentId()
    {
        var session = new TestSessionBuilder()
            .WithAgentId(AgentId.Claude)
            .Build();

        Assert.Equal(AgentId.Claude, session.AgentId);
    }

    [Fact]
    public void Build_WithSessionId_SetsSessionId()
    {
        var session = new TestSessionBuilder()
            .WithSessionId("my-session")
            .Build();

        Assert.Equal("my-session", session.SessionId);
    }

    [Fact]
    public void Build_DefaultSessionId_IsNonEmpty()
    {
        var session = new TestSessionBuilder().Build();

        Assert.False(string.IsNullOrEmpty(session.SessionId));
    }

    [Fact]
    public void Build_WithMetadata_SetsMetadata()
    {
        var metadata = new SessionMetadata { JobId = "job-1" };
        var session = new TestSessionBuilder()
            .WithMetadata(metadata)
            .Build();

        Assert.Equal("job-1", session.Metadata?.JobId);
    }

    [Fact]
    public async Task Build_WithTextEvents_EmitsEvents()
    {
        var session = new TestSessionBuilder()
            .AddText("Hello")
            .AddText("World")
            .Build();

        var events = await session.Events.ToList();
        var textEvents = events.OfType<TextEvent>().ToList();

        Assert.Equal(2, textEvents.Count);
        Assert.Equal("Hello", textEvents[0].Text);
        Assert.Equal("World", textEvents[1].Text);
    }

    [Fact]
    public async Task Build_WithToolCall_EmitsToolCallEvent()
    {
        var session = new TestSessionBuilder()
            .AddToolCall("Read", "{\"file_path\":\"/tmp/test\"}")
            .Build();

        var events = await session.Events.ToList();
        var toolCalls = events.OfType<ToolCallEvent>().ToList();

        Assert.Single(toolCalls);
        Assert.Equal("Read", toolCalls[0].ToolName);
        Assert.Contains("/tmp/test", toolCalls[0].InputJson);
    }

    [Fact]
    public async Task Build_WithResult_EmitsResultAsLastEvent()
    {
        var session = new TestSessionBuilder()
            .AddText("work")
            .WithResult(true, "Done")
            .Build();

        var events = await session.Events.ToList();
        var last = events.Last();

        Assert.IsType<ResultEvent>(last);
        Assert.True(((ResultEvent)last).IsSuccess);
        Assert.Equal("Done", ((ResultEvent)last).Response);
    }

    [Fact]
    public async Task Build_WithFailedResult_SetsExitCode()
    {
        var session = new TestSessionBuilder()
            .WithResult(false)
            .Build();

        Assert.NotNull(session.Result);
        Assert.False(session.Result.IsSuccess);
        Assert.Equal(1, session.Result.ExitCode);
    }

    [Fact]
    public async Task Build_WithUsage_IncludesUsageInResult()
    {
        var usage = new AgentUsage
        {
            InputTokens = 100,
            OutputTokens = 50,
            CostUsd = 0.01m,
        };

        var session = new TestSessionBuilder()
            .WithResult(true, usage: usage)
            .Build();

        Assert.NotNull(session.Result?.Usage);
        Assert.Equal(100, session.Result.Usage.InputTokens);
    }

    [Fact]
    public async Task WaitForCompletionAsync_ReturnsImmediately()
    {
        var session = new TestSessionBuilder()
            .WithResult(true)
            .Build();

        var result = await session.WaitForCompletionAsync();

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Build_DoesNotSupportPermission()
    {
        var session = new TestSessionBuilder().Build();

        Assert.False(session.SupportsPermissionResponse);
        Assert.False(session.SupportsQuestionResponse);
        Assert.False(session.SupportsMultiTurn);
    }

    [Fact]
    public async Task Build_RespondToPermission_Throws()
    {
        var session = new TestSessionBuilder().Build();

        await Assert.ThrowsAsync<NotSupportedException>(
            () => session.RespondToPermissionAsync("req", new PermissionDecision { Granted = true }));
    }

    [Fact]
    public async Task Build_Stop_IsNoop()
    {
        var session = new TestSessionBuilder().Build();
        await session.StopAsync();
    }

    [Fact]
    public async Task Build_Kill_IsNoop()
    {
        var session = new TestSessionBuilder().Build();
        await session.KillAsync();
    }

    [Fact]
    public async Task Build_DisposeAsync_IsIdempotent()
    {
        var session = new TestSessionBuilder().Build();
        await session.DisposeAsync();
        await session.DisposeAsync();
    }

    [Fact]
    public async Task Build_WithFileChange_EmitsEvent()
    {
        var session = new TestSessionBuilder()
            .AddFileChange("/src/main.cs", FileChangeKind.Modified, linesAdded: 5)
            .Build();

        var events = await session.Events.ToList();
        var fileChanges = events.OfType<FileChangeEvent>().ToList();

        Assert.Single(fileChanges);
        Assert.Equal("/src/main.cs", fileChanges[0].FilePath);
        Assert.Equal(5, fileChanges[0].LinesAdded);
    }

    [Fact]
    public async Task Build_WithError_EmitsErrorEvent()
    {
        var session = new TestSessionBuilder()
            .AddError("Something failed", isRetryable: true)
            .Build();

        var events = await session.Events.ToList();
        var errors = events.OfType<ErrorEvent>().ToList();

        Assert.Single(errors);
        Assert.Equal("Something failed", errors[0].Message);
        Assert.True(errors[0].IsRetryable);
    }

    [Fact]
    public async Task Build_WithThinking_EmitsThinkingEvent()
    {
        var session = new TestSessionBuilder()
            .AddThinking("Let me consider...")
            .Build();

        var events = await session.Events.ToList();
        var thinking = events.OfType<ThinkingEvent>().ToList();

        Assert.Single(thinking);
        Assert.Equal("Let me consider...", thinking[0].Content);
    }

    [Fact]
    public void Build_CompletedAt_IsSet()
    {
        var session = new TestSessionBuilder().Build();

        Assert.NotNull(session.CompletedAt);
        Assert.True(session.CompletedAt >= session.StartedAt);
    }

    [Fact]
    public void Build_RawOutput_IsNull()
    {
        var session = new TestSessionBuilder().Build();

        Assert.Null(session.RawOutput);
        Assert.Null(session.RawStderr);
    }
}
