using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Codex;

namespace Ivy.Tendril.Agents.Test.Codex;

public class CodexEventParserTests
{
    private readonly CodexEventParser _parser = new();

    [Fact]
    public void AgentId_IsCodex()
    {
        Assert.Equal(AgentId.Codex, _parser.AgentId);
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
    public void ParseLine_ValidJsonMissingType_ReturnsUnknownEvent()
    {
        var events = _parser.ParseLine("""{"foo": "bar"}""");
        Assert.Single(events);
        Assert.IsType<UnknownEvent>(events[0]);
    }

    [Fact]
    public void ParseLine_TurnStarted_ReturnsEmpty()
    {
        var json = """{"type":"turn.started","thread_id":"t1"}""";
        var events = _parser.ParseLine(json);
        Assert.Empty(events);
    }

    [Fact]
    public void ParseLine_ItemStarted_ReturnsEmpty()
    {
        var json = """{"type":"item.started","item":{"id":"i1"}}""";
        var events = _parser.ParseLine(json);
        Assert.Empty(events);
    }

    [Fact]
    public void ParseLine_ItemUpdated_ReturnsEmpty()
    {
        var json = """{"type":"item.updated","item":{"id":"item_1","type":"todo_list","items":[{"text":"Step one","completed":true},{"text":"Step two","completed":false}]}}""";
        var events = _parser.ParseLine(json);
        Assert.Empty(events);
    }

    [Fact]
    public void ParseLine_ThreadStarted_ReturnsSessionInitEvent()
    {
        var json = """{"type":"thread.started","thread_id":"thread_abc123"}""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var init = Assert.IsType<SessionInitEvent>(events[0]);
        Assert.Equal(AgentEventKind.SessionInit, init.Kind);
        Assert.Equal("thread_abc123", init.SessionId);
        Assert.Null(init.Model);
        Assert.Null(init.AvailableTools);
    }

    [Fact]
    public void ParseLine_ThreadStarted_NoThreadId_DefaultsToEmpty()
    {
        var json = """{"type":"thread.started"}""";
        var events = _parser.ParseLine(json);

        var init = Assert.IsType<SessionInitEvent>(events[0]);
        Assert.Equal("", init.SessionId);
    }

    [Fact]
    public void ParseLine_ItemCompleted_AgentMessage_ReturnsTextEvent()
    {
        var json = """{"type":"item.completed","item":{"id":"item_1","type":"agent_message","text":"Hello world"}}""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var textEvt = Assert.IsType<TextEvent>(events[0]);
        Assert.Equal(AgentEventKind.Text, textEvt.Kind);
        Assert.Equal("Hello world", textEvt.Text);
    }

    [Fact]
    public void ParseLine_ItemCompleted_AgentMessage_EmptyText_ReturnsEmpty()
    {
        var json = """{"type":"item.completed","item":{"id":"item_1","type":"agent_message","text":""}}""";
        var events = _parser.ParseLine(json);
        Assert.Empty(events);
    }

    [Fact]
    public void ParseLine_ItemCompleted_AgentMessage_NoText_ReturnsEmpty()
    {
        var json = """{"type":"item.completed","item":{"id":"item_1","type":"agent_message"}}""";
        var events = _parser.ParseLine(json);
        Assert.Empty(events);
    }

    [Fact]
    public void ParseLine_ItemCompleted_CommandExecution_ReturnsToolCallAndResult()
    {
        var json = """{"type":"item.completed","item":{"id":"cmd_1","type":"command_execution","command":"ls -la","aggregated_output":"file1\nfile2"}}""";
        var events = _parser.ParseLine(json);

        Assert.Equal(2, events.Count);

        var toolCall = Assert.IsType<ToolCallEvent>(events[0]);
        Assert.Equal(AgentEventKind.ToolCall, toolCall.Kind);
        Assert.Equal("cmd_1", toolCall.ToolUseId);
        Assert.Equal("bash", toolCall.ToolName);
        Assert.Contains("ls -la", toolCall.InputJson!);

        var toolResult = Assert.IsType<ToolResultEvent>(events[1]);
        Assert.Equal(AgentEventKind.ToolResult, toolResult.Kind);
        Assert.Equal("cmd_1", toolResult.ToolUseId);
        Assert.Equal("bash", toolResult.ToolName);
        Assert.Equal("file1\nfile2", toolResult.Output);
        Assert.False(toolResult.IsError);
    }

    [Fact]
    public void ParseLine_ItemCompleted_CommandExecution_NullOutput_SetsOutputNull()
    {
        var json = """{"type":"item.completed","item":{"id":"cmd_2","type":"command_execution","command":"echo hi"}}""";
        var events = _parser.ParseLine(json);

        Assert.Equal(2, events.Count);
        var toolResult = Assert.IsType<ToolResultEvent>(events[1]);
        Assert.Null(toolResult.Output);
    }

    [Fact]
    public void ParseLine_ItemCompleted_UnknownItemType_ReturnsUnknownEvent()
    {
        var json = """{"type":"item.completed","item":{"id":"x","type":"something_new"}}""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        Assert.IsType<UnknownEvent>(events[0]);
    }

    [Fact]
    public void ParseLine_ItemCompleted_WithoutItemProperty_ReturnsUnknownEvent()
    {
        var json = """{"type":"item.completed","data":"no item here"}""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        Assert.IsType<UnknownEvent>(events[0]);
    }

    [Fact]
    public void ParseLine_TurnCompleted_WithUsage_ReturnsResultEvent()
    {
        var json = """{"type":"turn.completed","usage":{"input_tokens":100,"output_tokens":50,"cached_input_tokens":25}}""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var result = Assert.IsType<ResultEvent>(events[0]);
        Assert.Equal(AgentEventKind.Result, result.Kind);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Usage);
        Assert.Equal(100, result.Usage!.InputTokens);
        Assert.Equal(50, result.Usage.OutputTokens);
        Assert.Equal(25, result.Usage.CacheReadTokens);
    }

    [Fact]
    public void ParseLine_TurnCompleted_WithoutUsage_ReturnsResultEventNullUsage()
    {
        var json = """{"type":"turn.completed"}""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var result = Assert.IsType<ResultEvent>(events[0]);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Usage);
    }

    [Fact]
    public void ParseLine_UnknownType_ReturnsUnknownEvent()
    {
        var json = """{"type":"some.future.event","data":"test"}""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        Assert.IsType<UnknownEvent>(events[0]);
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
            Usage = new AgentUsage { InputTokens = 10, OutputTokens = 5 },
        };

        var result = _parser.BuildResult([existing], 0);

        Assert.NotNull(result);
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.Usage!.InputTokens);
    }

    [Fact]
    public void BuildResult_WithoutResultEvent_CreatesSyntheticWithExitCode()
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
    public void BuildResult_ZeroExitCode_IsSuccess()
    {
        var result = _parser.BuildResult([], 0);

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void Reset_DoesNotThrow()
    {
        _parser.Reset();
    }

    [Fact]
    public void ParseLine_UnicodeContent_ParsesCorrectly()
    {
        var json = """{"type":"item.completed","item":{"id":"i1","type":"agent_message","text":"こんにちは世界"}}""";
        var events = _parser.ParseLine(json);

        var textEvt = Assert.IsType<TextEvent>(events[0]);
        Assert.Equal("こんにちは世界", textEvt.Text);
    }

    [Fact]
    public void ParseLine_PartialJsonTruncated_ReturnsUnknown()
    {
        var json = """{"type":"item.completed","item":{"id":"i""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        Assert.IsType<UnknownEvent>(events[0]);
    }
}
