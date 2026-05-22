using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Claude;

namespace Ivy.Tendril.Agents.Test.Claude;

public class ClaudeEventParserTests
{
    private readonly ClaudeEventParser _parser = new();

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
    public void ParseLine_SystemInit_ParsesCorrectly()
    {
        var json = """{"type":"system","subtype":"init","session_id":"abc-123","model":"claude-opus-4-6","tools":["Read","Write","Bash"],"cwd":"/tmp"}""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var init = Assert.IsType<SessionInitEvent>(events[0]);
        Assert.Equal(AgentEventKind.SessionInit, init.Kind);
        Assert.Equal("abc-123", init.SessionId);
        Assert.Equal("claude-opus-4-6", init.Model);
        Assert.Equal(3, init.AvailableTools!.Count);
        Assert.Contains("Read", init.AvailableTools);
        Assert.Contains("Write", init.AvailableTools);
        Assert.Contains("Bash", init.AvailableTools);
    }

    [Fact]
    public void ParseLine_SystemInit_WithEmptyTools_ParsesCorrectly()
    {
        var json = """{"type":"system","subtype":"init","session_id":"x","model":"sonnet","tools":[]}""";
        var events = _parser.ParseLine(json);

        var init = Assert.IsType<SessionInitEvent>(events[0]);
        Assert.Empty(init.AvailableTools!);
    }

    [Fact]
    public void ParseLine_HookStarted_ReturnsEmpty()
    {
        var json = """{"type":"system","subtype":"hook_started","hook_id":"h1","hook_name":"SessionStart:startup"}""";
        var events = _parser.ParseLine(json);
        Assert.Empty(events);
    }

    [Fact]
    public void ParseLine_HookResponse_ReturnsEmpty()
    {
        var json = """{"type":"system","subtype":"hook_response","hook_id":"h1","exit_code":0,"outcome":"success"}""";
        var events = _parser.ParseLine(json);
        Assert.Empty(events);
    }

    [Fact]
    public void ParseLine_SystemUnknownSubtype_ReturnsSystemEvent()
    {
        var json = """{"type":"system","subtype":"something_new","data":"test"}""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var sysEvt = Assert.IsType<SystemEvent>(events[0]);
        Assert.Equal("something_new", sysEvt.Subtype);
    }

    [Fact]
    public void ParseLine_AssistantTextBlock_ParsesText()
    {
        var json = """{"type":"assistant","message":{"id":"msg_01","model":"claude-opus-4-6","content":[{"type":"text","text":"Hello, world!"}],"usage":{"input_tokens":10,"output_tokens":5}}}""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var textEvt = Assert.IsType<TextEvent>(events[0]);
        Assert.Equal("Hello, world!", textEvt.Text);
    }

    [Fact]
    public void ParseLine_AssistantThinkingBlock_ParsesThinking()
    {
        var json = """{"type":"assistant","message":{"id":"msg_01","content":[{"type":"thinking","thinking":"Let me consider this..."}]}}""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var thinkEvt = Assert.IsType<ThinkingEvent>(events[0]);
        Assert.Equal("Let me consider this...", thinkEvt.Content);
    }

    [Fact]
    public void ParseLine_AssistantToolUse_ParsesToolCall()
    {
        var json = """{"type":"assistant","message":{"id":"msg_01","content":[{"type":"tool_use","id":"tu_01","name":"Read","input":{"file_path":"/tmp/test.txt"}}]}}""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var toolEvt = Assert.IsType<ToolCallEvent>(events[0]);
        Assert.Equal("tu_01", toolEvt.ToolUseId);
        Assert.Equal("Read", toolEvt.ToolName);
        Assert.NotNull(toolEvt.InputJson);
        Assert.Contains("file_path", toolEvt.InputJson);
    }

    [Fact]
    public void ParseLine_AssistantToolResult_ParsesResult()
    {
        var json = """{"type":"assistant","message":{"id":"msg_01","content":[{"type":"tool_result","tool_use_id":"tu_01","content":"file contents here","is_error":false}]}}""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var resultEvt = Assert.IsType<ToolResultEvent>(events[0]);
        Assert.Equal("tu_01", resultEvt.ToolUseId);
        Assert.Equal("file contents here", resultEvt.Output);
        Assert.False(resultEvt.IsError);
    }

    [Fact]
    public void ParseLine_AssistantToolResult_WithError_ParsesIsError()
    {
        var json = """{"type":"assistant","message":{"id":"msg_01","content":[{"type":"tool_result","tool_use_id":"tu_02","content":"Permission denied","is_error":true}]}}""";
        var events = _parser.ParseLine(json);

        var resultEvt = Assert.IsType<ToolResultEvent>(events[0]);
        Assert.True(resultEvt.IsError);
    }

    [Fact]
    public void ParseLine_AssistantToolResult_ContentAsArray_ExtractsText()
    {
        var json = """{"type":"assistant","message":{"id":"msg_01","content":[{"type":"tool_result","tool_use_id":"tu_03","content":[{"type":"text","text":"file contents here"}],"is_error":false}]}}""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var resultEvt = Assert.IsType<ToolResultEvent>(events[0]);
        Assert.Equal("tu_03", resultEvt.ToolUseId);
        Assert.Equal("file contents here", resultEvt.Output);
        Assert.False(resultEvt.IsError);
    }

    [Fact]
    public void ParseLine_AssistantToolResult_ContentAsArrayMultipleBlocks_ConcatenatesText()
    {
        var json = """{"type":"assistant","message":{"id":"msg_01","content":[{"type":"tool_result","tool_use_id":"tu_04","content":[{"type":"text","text":"line 1"},{"type":"text","text":"line 2"}],"is_error":false}]}}""";
        var events = _parser.ParseLine(json);

        var resultEvt = Assert.IsType<ToolResultEvent>(events[0]);
        Assert.Equal("line 1\nline 2", resultEvt.Output);
    }

    [Fact]
    public void ParseLine_AssistantToolResult_ContentNull_OutputIsNull()
    {
        var json = """{"type":"assistant","message":{"id":"msg_01","content":[{"type":"tool_result","tool_use_id":"tu_05","content":null,"is_error":false}]}}""";
        var events = _parser.ParseLine(json);

        var resultEvt = Assert.IsType<ToolResultEvent>(events[0]);
        Assert.Null(resultEvt.Output);
    }

    [Fact]
    public void ParseLine_AssistantToolResult_NoContentField_OutputIsNull()
    {
        var json = """{"type":"assistant","message":{"id":"msg_01","content":[{"type":"tool_result","tool_use_id":"tu_06","is_error":false}]}}""";
        var events = _parser.ParseLine(json);

        var resultEvt = Assert.IsType<ToolResultEvent>(events[0]);
        Assert.Null(resultEvt.Output);
    }

    [Fact]
    public void ParseLine_AssistantMultipleBlocks_ParsesAll()
    {
        var json = """{"type":"assistant","message":{"id":"msg_01","content":[{"type":"thinking","thinking":"planning"},{"type":"text","text":"answer"},{"type":"tool_use","id":"tu_01","name":"Bash","input":{"command":"ls"}}]}}""";
        var events = _parser.ParseLine(json);

        Assert.Equal(3, events.Count);
        Assert.IsType<ThinkingEvent>(events[0]);
        Assert.IsType<TextEvent>(events[1]);
        Assert.IsType<ToolCallEvent>(events[2]);
    }

    [Fact]
    public void ParseLine_Result_ParsesAllFields()
    {
        var json = """{"type":"result","subtype":"success","is_error":false,"duration_ms":2542,"num_turns":1,"result":"4","total_cost_usd":0.133,"usage":{"input_tokens":100,"output_tokens":50,"cache_read_input_tokens":200,"cache_creation_input_tokens":1000},"permission_denials":[]}""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var result = Assert.IsType<ResultEvent>(events[0]);
        Assert.True(result.IsSuccess);
        Assert.Equal("4", result.Response);
        Assert.Equal(1, result.TurnCount);
        Assert.NotNull(result.Duration);
        Assert.Equal(2542, result.Duration!.Value.TotalMilliseconds);
        Assert.NotNull(result.Usage);
        Assert.Equal(100, result.Usage.InputTokens);
        Assert.Equal(50, result.Usage.OutputTokens);
        Assert.Equal(200, result.Usage.CacheReadTokens);
        Assert.Equal(1000, result.Usage.CacheWriteTokens);
        Assert.Equal(0.133m, result.Usage.CostUsd);
        Assert.Empty(result.PermissionDenials);
    }

    [Fact]
    public void ParseLine_Result_WithError_ParsesCorrectly()
    {
        var json = """{"type":"result","subtype":"error","is_error":true,"duration_ms":500,"result":"Something failed"}""";
        var events = _parser.ParseLine(json);

        var result = Assert.IsType<ResultEvent>(events[0]);
        Assert.False(result.IsSuccess);
        Assert.Equal("Something failed", result.Response);
    }

    [Fact]
    public void ParseLine_Result_WithPermissionDenials_ParsesCorrectly()
    {
        var json = """{"type":"result","is_error":false,"duration_ms":1000,"result":"done","permission_denials":[{"tool_name":"Bash","tool_input":{"command":"rm -rf /"}},{"tool_name":"Write","tool_input":{"file_path":"/etc/passwd"}}]}""";
        var events = _parser.ParseLine(json);

        var result = Assert.IsType<ResultEvent>(events[0]);
        Assert.Equal(2, result.PermissionDenials.Count);
        Assert.Equal("Bash", result.PermissionDenials[0].ToolName);
        Assert.Contains("rm -rf", result.PermissionDenials[0].InputSummary!);
        Assert.Equal("Write", result.PermissionDenials[1].ToolName);
        Assert.Equal("/etc/passwd", result.PermissionDenials[1].InputSummary);
    }

    [Fact]
    public void ParseLine_UnknownType_ReturnsEmpty()
    {
        var json = """{"type":"something_new","data":"test"}""";
        var events = _parser.ParseLine(json);
        Assert.Empty(events);
    }

    [Fact]
    public void ParseLine_AssistantNoContent_ReturnsEmpty()
    {
        var json = """{"type":"assistant","message":{"id":"msg_01"}}""";
        var events = _parser.ParseLine(json);
        Assert.Empty(events);
    }

    [Fact]
    public void ParseLine_AssistantEmptyContent_ReturnsEmpty()
    {
        var json = """{"type":"assistant","message":{"id":"msg_01","content":[]}}""";
        var events = _parser.ParseLine(json);
        Assert.Empty(events);
    }

    [Fact]
    public void ParseLine_AssistantEmptyText_SkipsIt()
    {
        var json = """{"type":"assistant","message":{"id":"msg_01","content":[{"type":"text","text":""}]}}""";
        var events = _parser.ParseLine(json);
        Assert.Empty(events);
    }

    [Fact]
    public void ParseLine_AssistantEmptyThinking_SkipsIt()
    {
        var json = """{"type":"assistant","message":{"id":"msg_01","content":[{"type":"thinking","thinking":""}]}}""";
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
    public void BuildResult_WithExistingResultEvent_AddsExitCode()
    {
        var existing = new ResultEvent
        {
            Kind = AgentEventKind.Result,
            IsSuccess = true,
            Response = "done",
        };

        var result = _parser.BuildResult([existing], 0);

        Assert.NotNull(result);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("done", result.Response);
    }

    [Fact]
    public void BuildResult_WithNoResultEvent_CreatesSynthetic()
    {
        var events = new List<AgentEvent>
        {
            new TextEvent { Kind = AgentEventKind.Text, Text = "hello" }
        };

        var result = _parser.BuildResult(events, 1);

        Assert.NotNull(result);
        Assert.Equal(1, result.ExitCode);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void BuildResult_WithZeroExitCode_IsSuccess()
    {
        var result = _parser.BuildResult([], 0);

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void ParseLine_UnicodeContent_ParsesCorrectly()
    {
        var json = """{"type":"assistant","message":{"id":"msg_01","content":[{"type":"text","text":"こんにちは世界 🌍"}]}}""";
        var events = _parser.ParseLine(json);

        var textEvt = Assert.IsType<TextEvent>(events[0]);
        Assert.Equal("こんにちは世界 🌍", textEvt.Text);
    }

    [Fact]
    public void ParseLine_LongCommandPermissionDenial_TruncatesTo80Chars()
    {
        var longCmd = new string('x', 200);
        var json = $$$"""{"type":"result","is_error":false,"duration_ms":100,"result":"ok","permission_denials":[{"tool_name":"Bash","tool_input":{"command":"{{{longCmd}}}"}}]}""";
        var events = _parser.ParseLine(json);

        var result = Assert.IsType<ResultEvent>(events[0]);
        Assert.Equal(83, result.PermissionDenials[0].InputSummary!.Length); // 80 + "..."
    }

    [Fact]
    public void ParseLine_UserMessageToolResult_ParsesResult()
    {
        var json = """{"type":"user","message":{"role":"user","content":[{"tool_use_id":"toolu_01","type":"tool_result","content":"file contents here"}]}}""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var resultEvt = Assert.IsType<ToolResultEvent>(events[0]);
        Assert.Equal("toolu_01", resultEvt.ToolUseId);
        Assert.Equal("file contents here", resultEvt.Output);
        Assert.False(resultEvt.IsError);
    }

    [Fact]
    public void ParseLine_UserMessageToolResult_WithArrayContent_ParsesResult()
    {
        var json = """{"type":"user","message":{"role":"user","content":[{"tool_use_id":"toolu_02","type":"tool_result","content":[{"type":"text","text":"line 1"},{"type":"text","text":"line 2"}]}]}}""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var resultEvt = Assert.IsType<ToolResultEvent>(events[0]);
        Assert.Equal("toolu_02", resultEvt.ToolUseId);
        Assert.Equal("line 1\nline 2", resultEvt.Output);
    }

    [Fact]
    public void ParseLine_UserMessageToolResult_WithError_ParsesIsError()
    {
        var json = """{"type":"user","message":{"role":"user","content":[{"tool_use_id":"toolu_03","type":"tool_result","content":"Permission denied","is_error":true}]}}""";
        var events = _parser.ParseLine(json);

        var resultEvt = Assert.IsType<ToolResultEvent>(events[0]);
        Assert.True(resultEvt.IsError);
        Assert.Equal("Permission denied", resultEvt.Output);
    }

    [Fact]
    public void ParseLine_UserMessageWithoutToolResult_ReturnsEmpty()
    {
        var json = """{"type":"user","message":{"role":"user","content":[{"type":"text","text":"hello"}]}}""";
        var events = _parser.ParseLine(json);
        Assert.Empty(events);
    }

    [Fact]
    public void ParseLine_PartialJsonTruncated_ReturnsUnknown()
    {
        var json = """{"type":"assistant","message":{"id":"msg_01","content":[{"type":"te""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        Assert.IsType<UnknownEvent>(events[0]);
    }

    [Fact]
    public void ParseLine_SystemInit_NoSessionId_DefaultsToEmpty()
    {
        var json = """{"type":"system","subtype":"init","model":"test"}""";
        var events = _parser.ParseLine(json);

        var init = Assert.IsType<SessionInitEvent>(events[0]);
        Assert.Equal("", init.SessionId);
    }

    [Fact]
    public void ParseLine_Result_ZeroDuration_ReturnsNull()
    {
        var json = """{"type":"result","is_error":false,"duration_ms":0,"result":"fast"}""";
        var events = _parser.ParseLine(json);

        var result = Assert.IsType<ResultEvent>(events[0]);
        Assert.Null(result.Duration);
    }

    [Fact]
    public void ParseLine_Result_NoUsage_ReturnsNull()
    {
        var json = """{"type":"result","is_error":false,"result":"done"}""";
        var events = _parser.ParseLine(json);

        var result = Assert.IsType<ResultEvent>(events[0]);
        Assert.Null(result.Usage);
    }

    [Fact]
    public void Reset_DoesNotThrow()
    {
        _parser.Reset();
    }

    [Fact]
    public void AgentId_IsClaude()
    {
        Assert.Equal(Ivy.Tendril.Agents.Abstractions.AgentId.Claude, _parser.AgentId);
    }
}
