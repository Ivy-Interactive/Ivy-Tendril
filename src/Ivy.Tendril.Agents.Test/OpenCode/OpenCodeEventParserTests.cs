using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.OpenCode;

namespace Ivy.Tendril.Agents.Test.OpenCode;

public class OpenCodeEventParserTests
{
    private readonly OpenCodeEventParser _parser = new();

    [Fact]
    public void AgentId_IsOpenCode()
    {
        Assert.Equal(AgentId.OpenCode, _parser.AgentId);
    }

    [Fact]
    public void ParseLine_EmptyString_ReturnsEmpty()
    {
        var events = _parser.ParseLine("");
        Assert.Empty(events);
    }

    [Fact]
    public void ParseLine_NonJson_ReturnsEmpty()
    {
        var events = _parser.ParseLine("This is not JSON");
        Assert.Empty(events);
    }

    [Fact]
    public void ParseLine_MalformedJson_ReturnsUnknownEvent()
    {
        var events = _parser.ParseLine("{not valid json!!");
        Assert.Single(events);
        Assert.IsType<UnknownEvent>(events[0]);
        Assert.Equal(AgentEventKind.Unknown, events[0].Kind);
    }

    [Fact]
    public void ParseLine_JsonWithoutType_ReturnsUnknownEvent()
    {
        var events = _parser.ParseLine("""{"foo": "bar"}""");
        Assert.Single(events);
        Assert.IsType<UnknownEvent>(events[0]);
    }

    [Fact]
    public void ParseLine_StepStart_ReturnsSessionInitEvent()
    {
        var json = """{"type":"step_start","sessionID":"sess-abc-123"}""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var init = Assert.IsType<SessionInitEvent>(events[0]);
        Assert.Equal(AgentEventKind.SessionInit, init.Kind);
        Assert.Equal("sess-abc-123", init.SessionId);
        Assert.Null(init.Model);
        Assert.Null(init.AvailableTools);
    }

    [Fact]
    public void ParseLine_StepStart_NoSessionId_DefaultsToEmpty()
    {
        var json = """{"type":"step_start"}""";
        var events = _parser.ParseLine(json);

        var init = Assert.IsType<SessionInitEvent>(events[0]);
        Assert.Equal("", init.SessionId);
    }

    [Fact]
    public void ParseLine_Text_WithPartText_ReturnsTextEvent()
    {
        var json = """{"type":"text","part":{"text":"Hello, world!"}}""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var textEvt = Assert.IsType<TextEvent>(events[0]);
        Assert.Equal(AgentEventKind.Text, textEvt.Kind);
        Assert.Equal("Hello, world!", textEvt.Text);
    }

    [Fact]
    public void ParseLine_Text_WithEmptyText_ReturnsEmpty()
    {
        var json = """{"type":"text","part":{"text":""}}""";
        var events = _parser.ParseLine(json);

        Assert.Empty(events);
    }

    [Fact]
    public void ParseLine_Text_WithoutPart_ReturnsEmpty()
    {
        var json = """{"type":"text"}""";
        var events = _parser.ParseLine(json);

        Assert.Empty(events);
    }

    [Fact]
    public void ParseLine_ToolUse_ReturnsToolCallEvent()
    {
        var json = """{"type":"tool_use","part":{"tool":"read","callID":"call-001","state":{"input":{"file_path":"/tmp/test.txt"}}}}""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var toolEvt = Assert.IsType<ToolCallEvent>(events[0]);
        Assert.Equal(AgentEventKind.ToolCall, toolEvt.Kind);
        Assert.Equal("read", toolEvt.ToolName);
        Assert.Equal("call-001", toolEvt.ToolUseId);
        Assert.NotNull(toolEvt.InputJson);
        Assert.Contains("file_path", toolEvt.InputJson);
    }

    [Fact]
    public void ParseLine_ToolUse_WithCompletedStatus_ReturnsToolCallAndToolResult()
    {
        var json = """{"type":"tool_use","part":{"tool":"bash","callID":"call-002","state":{"input":{"command":"ls"},"status":"completed","output":"file1.txt\nfile2.txt"}}}""";
        var events = _parser.ParseLine(json);

        Assert.Equal(2, events.Count);
        var toolCall = Assert.IsType<ToolCallEvent>(events[0]);
        Assert.Equal("bash", toolCall.ToolName);
        Assert.Equal("call-002", toolCall.ToolUseId);

        var toolResult = Assert.IsType<ToolResultEvent>(events[1]);
        Assert.Equal(AgentEventKind.ToolResult, toolResult.Kind);
        Assert.Equal("call-002", toolResult.ToolUseId);
        Assert.Equal("bash", toolResult.ToolName);
        Assert.Equal("file1.txt\nfile2.txt", toolResult.Output);
        Assert.False(toolResult.IsError);
    }

    [Fact]
    public void ParseLine_ToolUse_WithoutPart_ReturnsEmpty()
    {
        var json = """{"type":"tool_use"}""";
        var events = _parser.ParseLine(json);

        Assert.Empty(events);
    }

    [Fact]
    public void ParseLine_StepFinish_WithReasonStop_ReturnsSuccessResult()
    {
        var json = """{"type":"step_finish","part":{"reason":"stop","cost":0.0042,"tokens":{"input":1000,"output":500}}}""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var result = Assert.IsType<ResultEvent>(events[0]);
        Assert.Equal(AgentEventKind.Result, result.Kind);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Usage);
        Assert.Equal(1000, result.Usage.InputTokens);
        Assert.Equal(500, result.Usage.OutputTokens);
        Assert.Equal(0.0042m, result.Usage.CostUsd);
    }

    [Fact]
    public void ParseLine_StepFinish_WithReasonNotStop_ReturnsFailureResult()
    {
        var json = """{"type":"step_finish","part":{"reason":"error"}}""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var result = Assert.IsType<ResultEvent>(events[0]);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void ParseLine_StepFinish_NoCostOrTokens_UsageIsNull()
    {
        var json = """{"type":"step_finish","part":{"reason":"stop"}}""";
        var events = _parser.ParseLine(json);

        var result = Assert.IsType<ResultEvent>(events[0]);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Usage);
    }

    [Fact]
    public void ParseLine_Error_ReturnsErrorEvent()
    {
        var json = """{"type":"error","error":{"data":{"message":"rate limit exceeded","isRetryable":true,"statusCode":429}}}""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var errorEvt = Assert.IsType<ErrorEvent>(events[0]);
        Assert.Equal(AgentEventKind.Error, errorEvt.Kind);
        Assert.Equal("rate limit exceeded", errorEvt.Message);
        Assert.True(errorEvt.IsRetryable);
        Assert.False(errorEvt.IsAuthError);
    }

    [Fact]
    public void ParseLine_Error_AuthStatusCode401_SetsIsAuthError()
    {
        var json = """{"type":"error","error":{"data":{"message":"unauthorized","isRetryable":false,"statusCode":401}}}""";
        var events = _parser.ParseLine(json);

        var errorEvt = Assert.IsType<ErrorEvent>(events[0]);
        Assert.True(errorEvt.IsAuthError);
        Assert.False(errorEvt.IsRetryable);
    }

    [Fact]
    public void ParseLine_Error_AuthStatusCode403_SetsIsAuthError()
    {
        var json = """{"type":"error","error":{"data":{"message":"forbidden","isRetryable":false,"statusCode":403}}}""";
        var events = _parser.ParseLine(json);

        var errorEvt = Assert.IsType<ErrorEvent>(events[0]);
        Assert.True(errorEvt.IsAuthError);
    }

    [Fact]
    public void ParseLine_Error_SetsHasErrorState()
    {
        var json = """{"type":"error","error":{"data":{"message":"something went wrong","isRetryable":false,"statusCode":500}}}""";
        _parser.ParseLine(json);

        // BuildResult should override success based on _hasError
        var result = _parser.BuildResult([], 0);
        Assert.False(result!.IsSuccess);
    }

    [Fact]
    public void ParseLine_Error_WithoutData_FallsBackToMessage()
    {
        var json = """{"type":"error","error":{"message":"simple error"}}""";
        var events = _parser.ParseLine(json);

        var errorEvt = Assert.IsType<ErrorEvent>(events[0]);
        Assert.Equal("simple error", errorEvt.Message);
        Assert.False(errorEvt.IsRetryable);
        Assert.False(errorEvt.IsAuthError);
    }

    [Fact]
    public void ParseLine_UnknownType_ReturnsEmpty()
    {
        var json = """{"type":"something_new","data":"test"}""";
        var events = _parser.ParseLine(json);
        Assert.Empty(events);
    }

    [Fact]
    public void Flush_ReturnsEmpty()
    {
        var events = _parser.Flush();
        Assert.Empty(events);
    }

    [Fact]
    public void BuildResult_AfterErrorEvent_OverridesIsSuccessToFalse()
    {
        // Parse an error event to set _hasError
        _parser.ParseLine("""{"type":"error","error":{"data":{"message":"fail","isRetryable":false,"statusCode":500}}}""");

        // Even with a ResultEvent that says success, _hasError overrides it
        var resultEvent = new ResultEvent
        {
            Kind = AgentEventKind.Result,
            IsSuccess = true,
        };

        var result = _parser.BuildResult([resultEvent], 0);

        Assert.NotNull(result);
        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void BuildResult_WithoutError_UsesExitCode()
    {
        var result = _parser.BuildResult([], 0);

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void BuildResult_WithoutError_NonZeroExitCode_IsNotSuccess()
    {
        var result = _parser.BuildResult([], 1);

        Assert.NotNull(result);
        Assert.False(result.IsSuccess);
        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public void BuildResult_WithExistingResultEvent_AddsExitCode()
    {
        var existing = new ResultEvent
        {
            Kind = AgentEventKind.Result,
            IsSuccess = true,
            Usage = new AgentUsage { InputTokens = 100, OutputTokens = 50 },
        };

        var result = _parser.BuildResult([existing], 0);

        Assert.NotNull(result);
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.IsSuccess);
        Assert.Equal(100, result.Usage!.InputTokens);
    }

    [Fact]
    public void Reset_ClearsHasErrorState()
    {
        // Set _hasError
        _parser.ParseLine("""{"type":"error","error":{"data":{"message":"fail","isRetryable":false,"statusCode":500}}}""");

        // Reset clears it
        _parser.Reset();

        // Now BuildResult should respect exit code, not _hasError
        var result = _parser.BuildResult([], 0);
        Assert.True(result!.IsSuccess);
    }
}
