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
    public void ParseLine_EmptyString_ReturnsEmpty()
    {
        var events = _parser.ParseLine("");
        Assert.Empty(events);
    }

    [Fact]
    public void ParseLine_Whitespace_ReturnsEmpty()
    {
        var events = _parser.ParseLine("   ");
        Assert.Empty(events);
    }

    [Fact]
    public void ParseLine_NonJson_ReturnsEmpty()
    {
        var events = _parser.ParseLine("Line 1");
        Assert.Empty(events);
    }

    [Fact]
    public void ParseLine_StderrPrefix_ReturnsEmpty()
    {
        var events = _parser.ParseLine("[stderr] warning: conversation \"2c358e24-748c-4bc1-8df8-37b0f2648a4f\" not found");
        Assert.Empty(events);
    }

    [Fact]
    public void ParseLine_MalformedJson_ReturnsUnknownEvent()
    {
        var events = _parser.ParseLine("{not valid json!!");
        Assert.Single(events);
        Assert.IsType<UnknownEvent>(events[0]);
    }

    [Fact]
    public void ParseLine_StepUpdate_AgentResponse_ReturnsTextEvent()
    {
        var json = "{\"event\":\"step_update\",\"step_update\":{\"step_type\":\"agent_response\",\"text_delta\":\"Hello world\"}}";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var textEvent = Assert.IsType<TextEvent>(events[0]);
        Assert.Equal(AgentEventKind.Text, textEvent.Kind);
        Assert.Equal("Hello world", textEvent.Text);
        Assert.True(textEvent.IsDelta);
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
        Assert.Equal(2, initEvent.AvailableTools?.Count ?? 0);
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
