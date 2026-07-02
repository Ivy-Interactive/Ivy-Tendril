using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Antigravity;

namespace Ivy.Tendril.Agents.Test.Antigravity;

public class AntigravityEventParserTests
{
    private readonly AntigravityEventParser _parser = new();

    [Fact]
    public void AgentId_IsAntigravity()
    {
        Assert.Equal(AgentId.Antigravity, _parser.AgentId);
    }

    [Fact]
    public void ParseLine_BufferLines_ReturnsEmpty()
    {
        var events1 = _parser.ParseLine("Line 1");
        var events2 = _parser.ParseLine("");
        var events3 = _parser.ParseLine("Line 2");

        Assert.Empty(events1);
        Assert.Empty(events2);
        Assert.Empty(events3);
    }

    [Fact]
    public void Flush_EmptyBuffer_ReturnsEmpty()
    {
        var events = _parser.Flush();
        Assert.Empty(events);
    }

    [Fact]
    public void Flush_AccumulatedContent_ReturnsSessionInitTextAndResultEvents()
    {
        _parser.ParseLine("Hello");
        _parser.ParseLine("World");

        var events = _parser.Flush();

        Assert.Equal(3, events.Count);

        var initEvent = Assert.IsType<SessionInitEvent>(events[0]);
        Assert.Equal(AgentEventKind.SessionInit, initEvent.Kind);
        Assert.Equal("", initEvent.SessionId);

        var textEvent = Assert.IsType<TextEvent>(events[1]);
        Assert.Equal(AgentEventKind.Text, textEvent.Kind);
        Assert.Equal($"Hello{Environment.NewLine}World", textEvent.Text);

        var resultEvent = Assert.IsType<ResultEvent>(events[2]);
        Assert.Equal(AgentEventKind.Result, resultEvent.Kind);
        Assert.True(resultEvent.IsSuccess);
        Assert.Equal($"Hello{Environment.NewLine}World", resultEvent.Response);

        // Buffer should be cleared after flush
        Assert.Empty(_parser.Flush());
    }

    [Fact]
    public void Reset_ClearsBuffer()
    {
        _parser.ParseLine("Some content");
        _parser.Reset();

        var events = _parser.Flush();
        Assert.Empty(events);
    }

    [Fact]
    public void BuildResult_WithExistingResultEvent_UpdatesExitCode()
    {
        var events = new List<AgentEvent>
        {
            new ResultEvent
            {
                Kind = AgentEventKind.Result,
                IsSuccess = true,
                Response = "Done"
            }
        };

        var updatedResult = _parser.BuildResult(events, 1);

        Assert.NotNull(updatedResult);
        Assert.Equal(1, updatedResult.ExitCode);
        Assert.True(updatedResult.IsSuccess); // Matches existing
        Assert.Equal("Done", updatedResult.Response);
    }

    [Fact]
    public void BuildResult_WithoutExistingResultEvent_CreatesNewResultEvent()
    {
        var events = new List<AgentEvent>();

        var result = _parser.BuildResult(events, 0);

        Assert.NotNull(result);
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.IsSuccess);

        var failedResult = _parser.BuildResult(events, 2);

        Assert.NotNull(failedResult);
        Assert.Equal(2, failedResult.ExitCode);
        Assert.False(failedResult.IsSuccess);
    }
}
