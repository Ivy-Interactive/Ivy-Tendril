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
    public void ParseLine_FirstLine_ReturnsSessionInitAndTextEvents()
    {
        var events = _parser.ParseLine("Line 1");

        Assert.Equal(2, events.Count);

        var initEvent = Assert.IsType<SessionInitEvent>(events[0]);
        Assert.Equal(AgentEventKind.SessionInit, initEvent.Kind);

        var textEvent = Assert.IsType<TextEvent>(events[1]);
        Assert.Equal(AgentEventKind.Text, textEvent.Kind);
        Assert.Equal("Line 1\n", textEvent.Text);
        Assert.False(textEvent.IsDelta);
    }

    [Fact]
    public void ParseLine_SubsequentLines_ReturnsTextEventOnly()
    {
        _parser.ParseLine("Line 1");
        var events = _parser.ParseLine("Line 2");

        Assert.Single(events);

        var textEvent = Assert.IsType<TextEvent>(events[0]);
        Assert.Equal(AgentEventKind.Text, textEvent.Kind);
        Assert.Equal("Line 2\n", textEvent.Text);
        Assert.False(textEvent.IsDelta);
    }

    [Fact]
    public void Flush_AfterLines_ReturnsResultEventOnly()
    {
        _parser.ParseLine("Line 1");
        var events = _parser.Flush();

        Assert.Single(events);
        var resultEvent = Assert.IsType<ResultEvent>(events[0]);
        Assert.Equal(AgentEventKind.Result, resultEvent.Kind);
        Assert.True(resultEvent.IsSuccess);
    }

    [Fact]
    public void Flush_NoLines_ReturnsSessionInitAndResultEvents()
    {
        var events = _parser.Flush();

        Assert.Equal(2, events.Count);
        var initEvent = Assert.IsType<SessionInitEvent>(events[0]);
        var resultEvent = Assert.IsType<ResultEvent>(events[1]);
        Assert.True(resultEvent.IsSuccess);
    }

    [Fact]
    public void Reset_ResetsInitializedState()
    {
        _parser.ParseLine("Line 1");
        _parser.Reset();

        // After reset, the next line should trigger SessionInit again
        var events = _parser.ParseLine("Line 2");
        Assert.Equal(2, events.Count);
        Assert.IsType<SessionInitEvent>(events[0]);
        Assert.IsType<TextEvent>(events[1]);
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
