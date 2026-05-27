using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Copilot;

namespace Ivy.Tendril.Agents.Test.Copilot;

public class CopilotEventParserTests
{
    private readonly CopilotEventParser _parser = new();

    [Fact]
    public void AgentId_IsCopilot()
    {
        Assert.Equal(AgentId.Copilot, _parser.AgentId);
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
    public void ParseLine_EphemeralTrue_ReturnsEmpty()
    {
        var json = """{"type":"assistant.message","ephemeral":true,"data":{"content":"streaming"}}""";
        var events = _parser.ParseLine(json);
        Assert.Empty(events);
    }

    [Fact]
    public void ParseLine_EphemeralTrueWithSpace_ReturnsEmpty()
    {
        var json = """{"type":"assistant.message","ephemeral": true,"data":{"content":"streaming"}}""";
        var events = _parser.ParseLine(json);
        Assert.Empty(events);
    }

    [Theory]
    [InlineData("session.mcp_server_status_changed")]
    [InlineData("session.mcp_servers_loaded")]
    [InlineData("session.skills_loaded")]
    [InlineData("user.message")]
    [InlineData("assistant.turn_start")]
    [InlineData("assistant.turn_end")]
    [InlineData("assistant.message_start")]
    [InlineData("assistant.message_delta")]
    [InlineData("assistant.reasoning")]
    public void ParseLine_SkippedTypes_ReturnsEmpty(string type)
    {
        var json = $$$"""{"type":"{{{type}}}","data":{}}""";
        var events = _parser.ParseLine(json);
        Assert.Empty(events);
    }

    [Fact]
    public void ParseLine_SessionToolsUpdated_ReturnsSessionInitEvent()
    {
        var json = """{"type":"session.tools_updated","id":"sess-123","data":{"model":"gpt-4o","tools":["view","apply_patch","powershell"]}}""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var init = Assert.IsType<SessionInitEvent>(events[0]);
        Assert.Equal(AgentEventKind.SessionInit, init.Kind);
        Assert.Equal("sess-123", init.SessionId);
        Assert.Equal("gpt-4o", init.Model);
        Assert.NotNull(init.AvailableTools);
        Assert.Equal(3, init.AvailableTools!.Count);
        Assert.Contains("view", init.AvailableTools);
        Assert.Contains("apply_patch", init.AvailableTools);
        Assert.Contains("powershell", init.AvailableTools);
    }

    [Fact]
    public void ParseLine_SessionToolsUpdated_NoTools_ReturnsNullToolsList()
    {
        var json = """{"type":"session.tools_updated","data":{"model":"gpt-4o"}}""";
        var events = _parser.ParseLine(json);

        var init = Assert.IsType<SessionInitEvent>(events[0]);
        Assert.Null(init.AvailableTools);
    }

    [Fact]
    public void ParseLine_SessionToolsUpdated_EmptyTools_ReturnsNullToolsList()
    {
        var json = """{"type":"session.tools_updated","data":{"model":"gpt-4o","tools":[]}}""";
        var events = _parser.ParseLine(json);

        var init = Assert.IsType<SessionInitEvent>(events[0]);
        Assert.Null(init.AvailableTools);
    }

    [Fact]
    public void ParseLine_AssistantMessage_WithContent_ReturnsTextEvent()
    {
        var json = """{"type":"assistant.message","data":{"content":"Hello, world!"}}""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var textEvt = Assert.IsType<TextEvent>(events[0]);
        Assert.Equal(AgentEventKind.Text, textEvt.Kind);
        Assert.Equal("Hello, world!", textEvt.Text);
    }

    [Fact]
    public void ParseLine_AssistantMessage_WithToolRequests_ReturnsToolCallEvents()
    {
        var json = """{"type":"assistant.message","data":{"toolRequests":[{"toolCallId":"tc-1","tool":"view","parameters":{"path":"/tmp/test.txt"}},{"toolCallId":"tc-2","tool":"glob","parameters":{"pattern":"*.cs"}}]}}""";
        var events = _parser.ParseLine(json);

        Assert.Equal(2, events.Count);
        var tc1 = Assert.IsType<ToolCallEvent>(events[0]);
        Assert.Equal("tc-1", tc1.ToolUseId);
        Assert.Equal("view", tc1.ToolName);
        Assert.Contains("path", tc1.InputJson!);

        var tc2 = Assert.IsType<ToolCallEvent>(events[1]);
        Assert.Equal("tc-2", tc2.ToolUseId);
        Assert.Equal("glob", tc2.ToolName);
    }

    [Fact]
    public void ParseLine_AssistantMessage_ReportIntentToolRequest_IsSkipped()
    {
        var json = """{"type":"assistant.message","data":{"toolRequests":[{"toolCallId":"tc-meta","tool":"report_intent","parameters":{"intent":"reading files"}}]}}""";
        var events = _parser.ParseLine(json);

        Assert.Empty(events);
    }

    [Fact]
    public void ParseLine_AssistantMessage_BothContentAndToolRequests_ReturnsBoth()
    {
        var json = """{"type":"assistant.message","data":{"content":"Checking the file...","toolRequests":[{"toolCallId":"tc-1","tool":"view","parameters":{"path":"/f.txt"}}]}}""";
        var events = _parser.ParseLine(json);

        Assert.Equal(2, events.Count);
        Assert.IsType<TextEvent>(events[0]);
        Assert.IsType<ToolCallEvent>(events[1]);
    }

    [Fact]
    public void ParseLine_ToolExecutionComplete_ReturnsToolResultEvent()
    {
        var json = """{"type":"tool.execution_complete","data":{"toolCallId":"tc-1","result":{"content":"file contents here"}}}""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var resultEvt = Assert.IsType<ToolResultEvent>(events[0]);
        Assert.Equal(AgentEventKind.ToolResult, resultEvt.Kind);
        Assert.Equal("tc-1", resultEvt.ToolUseId);
        Assert.Equal("file contents here", resultEvt.Output);
        Assert.False(resultEvt.IsError);
    }

    [Fact]
    public void ParseLine_Result_ReturnsResultEvent()
    {
        var json = """{"type":"result","exitCode":0,"usage":{"premiumRequests":5,"sessionDurationMs":12345}}""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var result = Assert.IsType<ResultEvent>(events[0]);
        Assert.Equal(AgentEventKind.Result, result.Kind);
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(result.Duration);
        Assert.Equal(12345, result.Duration!.Value.TotalMilliseconds);
        Assert.NotNull(result.Usage);
        Assert.Equal(5, result.Usage.PremiumRequests);
    }

    [Fact]
    public void ParseLine_Result_NonZeroExitCode_IsNotSuccess()
    {
        var json = """{"type":"result","exitCode":1}""";
        var events = _parser.ParseLine(json);

        var result = Assert.IsType<ResultEvent>(events[0]);
        Assert.False(result.IsSuccess);
        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public void ParseLine_UnknownType_ReturnsEmpty()
    {
        var json = """{"type":"some.future.event","data":{"foo":"bar"}}""";
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
    public void Reset_DoesNotThrow()
    {
        _parser.Reset();
    }
}
