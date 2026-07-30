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
    public void ParseLine_TextLine_ReturnsTextEvent()
    {
        var events = _parser.ParseLine("Line 1");

        Assert.Single(events);
        var textEvent = Assert.IsType<TextEvent>(events[0]);
        Assert.Equal(AgentEventKind.Text, textEvent.Kind);
        Assert.Equal("Line 1\n", textEvent.Text);
        Assert.False(textEvent.IsDelta);
    }

    [Fact]
    public void ParseLine_InitJson_ReturnsSessionInitEvent()
    {
        var json = "{\"event\":\"init\",\"conversation_id\":\"c-123\",\"init\":{\"model\":\"gemini-3.6-flash\",\"tools\":[\"read\",\"write\"]}}";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var initEvent = Assert.IsType<SessionInitEvent>(events[0]);
        Assert.Equal("c-123", initEvent.SessionId);
        Assert.Equal("gemini-3.6-flash", initEvent.Model);
        Assert.Equal(2, initEvent.AvailableTools.Count);
    }

    [Fact]
    public void ParseLine_ResultJson_ReturnsResultEvent()
    {
        var json = "{\"event\":\"result\",\"result\":{\"conversation_id\":\"c-123\",\"status\":\"SUCCESS\",\"response\":\"Done\",\"duration_seconds\":10.5,\"usage\":{\"input_tokens\":100,\"output_tokens\":50}}}";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var resultEvent = Assert.IsType<ResultEvent>(events[0]);
        Assert.True(resultEvent.IsSuccess);
        Assert.Equal("Done", resultEvent.Response);
        Assert.Equal(100, resultEvent.Usage?.InputTokens);
    }

    [Fact]
    public void Flush_ReturnsEmptyList()
    {
        _parser.ParseLine("Line 1");
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
        Assert.True(updatedResult.IsSuccess);
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
