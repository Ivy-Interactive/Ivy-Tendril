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
        Assert.Null(resultEvent.Error);
        Assert.Equal(100, resultEvent.Usage?.InputTokens);
    }

    [Fact]
    public void ParseLine_ResultJson_WithFullUsageAndCost_ExtractsAllFields()
    {
        var json = "{\"event\":\"result\",\"result\":{\"conversation_id\":\"c-123\",\"status\":\"SUCCESS\",\"model\":\"gemini-3.7-flash\",\"total_cost_usd\":0.0125,\"usage\":{\"input_tokens\":1000,\"output_tokens\":500,\"cache_read_tokens\":200,\"cache_write_tokens\":50,\"reasoning_tokens\":80}}}";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var resultEvent = Assert.IsType<ResultEvent>(events[0]);
        Assert.NotNull(resultEvent.Usage);
        Assert.Equal(1000, resultEvent.Usage.InputTokens);
        Assert.Equal(500, resultEvent.Usage.OutputTokens);
        Assert.Equal(200, resultEvent.Usage.CacheReadTokens);
        Assert.Equal(50, resultEvent.Usage.CacheWriteTokens);
        Assert.Equal(80, resultEvent.Usage.ReasoningTokens);
        Assert.Equal(0.0125m, resultEvent.Usage.CostUsd);
        Assert.Equal("gemini-3.7-flash", resultEvent.Usage.Model);
    }

    [Fact]
    public void ParseLine_InitFollowedByResultWithoutModel_AttachesInitModelToResultUsage()
    {
        var parser = new AntigravityEventParser();
        var initJson = "{\"event\":\"init\",\"conversation_id\":\"c-123\",\"init\":{\"model\":\"gemini-3.6-flash\"}}";
        var resultJson = "{\"event\":\"result\",\"result\":{\"status\":\"SUCCESS\",\"usage\":{\"input_tokens\":200,\"output_tokens\":100}}}";

        parser.ParseLine(initJson);
        var events = parser.ParseLine(resultJson);

        Assert.Single(events);
        var resultEvent = Assert.IsType<ResultEvent>(events[0]);
        Assert.NotNull(resultEvent.Usage);
        Assert.Equal("gemini-3.6-flash", resultEvent.Usage.Model);
        Assert.Equal(200, resultEvent.Usage.InputTokens);
        Assert.Equal(100, resultEvent.Usage.OutputTokens);
    }

    [Fact]
    public void ParseLine_ResultJson_WithRecoveredToolErrorAndResponse_SetsIsSuccessTrueAndErrorNull()
    {
        var json = "{\"event\":\"result\",\"result\":{\"conversation_id\":\"c-123\",\"status\":\"ERROR\",\"error\":\"declaring permissions: cortex tool write_to_file: path is not valid\",\"response\":\"I have finished the plan execution\",\"duration_seconds\":15.2}}";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var resultEvent = Assert.IsType<ResultEvent>(events[0]);
        Assert.True(resultEvent.IsSuccess);
        Assert.Null(resultEvent.Error);
        Assert.Equal("I have finished the plan execution", resultEvent.Response);
    }

    [Fact]
    public void ParseLine_ResultJson_WithFatalErrorAndNoResponse_SetsErrorAndIsSuccessFalse()
    {
        var json = "{\"event\":\"result\",\"result\":{\"conversation_id\":\"c-123\",\"status\":\"ERROR\",\"error\":\"fatal: unhandled exception\",\"response\":\"\",\"duration_seconds\":5.0}}";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var resultEvent = Assert.IsType<ResultEvent>(events[0]);
        Assert.False(resultEvent.IsSuccess);
        Assert.Equal("fatal: unhandled exception", resultEvent.Error);
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

    [Fact]
    public void ParseLine_ToolActiveFollowedByDone_YieldsCallThenResult()
    {
        var parser = new AntigravityEventParser();
        var activeJson = "{\"event\":\"step_update\",\"step_update\":{\"step_type\":\"tool\",\"state\":\"ACTIVE\",\"step_index\":1,\"tool_name\":\"read_file\",\"tool_info\":{\"parameters\":{\"path\":\"test.txt\"}}}}";
        var doneJson = "{\"event\":\"step_update\",\"step_update\":{\"step_type\":\"tool\",\"state\":\"DONE\",\"step_index\":1,\"tool_name\":\"read_file\",\"tool_info\":{\"output\":\"file contents\"}}}";

        var activeEvents = parser.ParseLine(activeJson);
        Assert.Single(activeEvents);
        var toolCall = Assert.IsType<ToolCallEvent>(activeEvents[0]);
        Assert.Equal("read_file", toolCall.ToolName);
        var toolUseId = toolCall.ToolUseId;
        Assert.NotNull(toolUseId);

        var doneEvents = parser.ParseLine(doneJson);
        Assert.Single(doneEvents);
        var toolResult = Assert.IsType<ToolResultEvent>(doneEvents[0]);
        Assert.Equal(toolUseId, toolResult.ToolUseId);
        Assert.Equal("read_file", toolResult.ToolName);
        Assert.Equal("file contents", toolResult.Output);
        Assert.False(toolResult.IsError);
    }

    [Fact]
    public void ParseLine_TwoActiveStepsWithMissingStepIndex_GetDistinctIds()
    {
        var parser = new AntigravityEventParser();
        var active1 = "{\"event\":\"step_update\",\"step_update\":{\"step_type\":\"tool\",\"state\":\"ACTIVE\",\"tool_name\":\"bash\"}}";
        var active2 = "{\"event\":\"step_update\",\"step_update\":{\"step_type\":\"tool\",\"state\":\"ACTIVE\",\"tool_name\":\"grep\"}}";

        var events1 = parser.ParseLine(active1);
        Assert.Single(events1);
        var call1 = Assert.IsType<ToolCallEvent>(events1[0]);
        var id1 = call1.ToolUseId;

        var events2 = parser.ParseLine(active2);
        Assert.Single(events2);
        var call2 = Assert.IsType<ToolCallEvent>(events2[0]);
        var id2 = call2.ToolUseId;

        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void ParseLine_TerminalStateNotDone_YieldsResultWithIsErrorTrue()
    {
        var parser = new AntigravityEventParser();
        var activeJson = "{\"event\":\"step_update\",\"step_update\":{\"step_type\":\"tool\",\"state\":\"ACTIVE\",\"step_index\":5,\"tool_name\":\"write_file\"}}";
        var cancelledJson = "{\"event\":\"step_update\",\"step_update\":{\"step_type\":\"tool\",\"state\":\"CANCELLED\",\"step_index\":5,\"tool_name\":\"write_file\"}}";

        parser.ParseLine(activeJson);
        var events = parser.ParseLine(cancelledJson);

        Assert.Single(events);
        var result = Assert.IsType<ToolResultEvent>(events[0]);
        Assert.True(result.IsError);
        Assert.Contains("CANCELLED", result.Output);
    }

    [Fact]
    public void ParseLine_DoneWithNoPrecedingActive_YieldsBothCallAndResult()
    {
        var parser = new AntigravityEventParser();
        var doneJson = "{\"event\":\"step_update\",\"step_update\":{\"step_type\":\"tool\",\"state\":\"DONE\",\"step_index\":3,\"tool_name\":\"find_by_name\",\"tool_info\":{\"output\":\"found 5 files\"}}}";

        var events = parser.ParseLine(doneJson);

        Assert.Equal(2, events.Count);

        var toolCall = Assert.IsType<ToolCallEvent>(events[0]);
        Assert.Equal("find_by_name", toolCall.ToolName);
        var toolUseId = toolCall.ToolUseId;

        var toolResult = Assert.IsType<ToolResultEvent>(events[1]);
        Assert.Equal(toolUseId, toolResult.ToolUseId);
        Assert.Equal("find_by_name", toolResult.ToolName);
        Assert.Equal("found 5 files", toolResult.Output);
        Assert.False(toolResult.IsError);
    }

    [Fact]
    public void ParseLine_StepIndexAsJsonString_ParsesWithoutThrowing()
    {
        var parser = new AntigravityEventParser();
        var json = "{\"event\":\"step_update\",\"step_update\":{\"step_type\":\"tool\",\"state\":\"ACTIVE\",\"step_index\":\"42\",\"tool_name\":\"test_tool\"}}";

        var events = parser.ParseLine(json);

        Assert.Single(events);
        var toolCall = Assert.IsType<ToolCallEvent>(events[0]);
        Assert.Equal("test_tool", toolCall.ToolName);
        Assert.NotNull(toolCall.ToolUseId);
    }
}
