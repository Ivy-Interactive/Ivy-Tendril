using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Gemini;

namespace Ivy.Tendril.Agents.Test.Gemini;

public class GeminiEventParserTests
{
    private readonly GeminiEventParser _parser = new();

    [Fact]
    public void AgentId_IsGemini()
    {
        Assert.Equal(AgentId.Gemini, _parser.AgentId);
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
    }

    [Fact]
    public void ParseLine_StderrPrefix_ReturnsEmpty()
    {
        var events = _parser.ParseLine("[stderr] Warning: Basic terminal detected");
        Assert.Empty(events);
    }

    [Fact]
    public void ParseLine_InitEvent_ReturnsSessionInitEvent()
    {
        var json = """{"type":"init","timestamp":"2026-05-28T17:12:52.341Z","session_id":"52fc4c72-30ad-45f0-9f40-cd91e2fcc9a9","model":"gemini-3-flash-preview"}""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var init = Assert.IsType<SessionInitEvent>(events[0]);
        Assert.Equal("52fc4c72-30ad-45f0-9f40-cd91e2fcc9a9", init.SessionId);
        Assert.Equal("gemini-3-flash-preview", init.Model);
    }

    [Fact]
    public void ParseLine_MessageEvent_UserRole_ReturnsEmpty()
    {
        var json = """{"type":"message","timestamp":"2026-05-28T17:12:53.016Z","role":"user","content":"the prompt text"}""";
        var events = _parser.ParseLine(json);
        Assert.Empty(events);
    }

    [Fact]
    public void ParseLine_MessageEvent_AssistantRole_ReturnsTextEvent()
    {
        var json = """{"type":"message","timestamp":"2026-05-28T17:13:00.000Z","role":"assistant","content":"Here is my response"}""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var text = Assert.IsType<TextEvent>(events[0]);
        Assert.Equal("Here is my response", text.Text);
    }

    [Fact]
    public void ParseLine_MessageEvent_NoRole_ReturnsTextEvent()
    {
        var json = """{"type":"message","content":"Some text"}""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var text = Assert.IsType<TextEvent>(events[0]);
        Assert.Equal("Some text", text.Text);
    }

    [Fact]
    public void ParseLine_ToolUseEvent_ReturnsToolCallEvent()
    {
        var json = """{"type":"tool_use","timestamp":"2026-05-28T17:12:56.503Z","tool_name":"list_directory","tool_id":"list_directory__list_directory_123_1","parameters":{"dir_path":"D:\\src\\tools"}}""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var toolCall = Assert.IsType<ToolCallEvent>(events[0]);
        Assert.Equal("list_directory__list_directory_123_1", toolCall.ToolUseId);
        Assert.Equal("list_directory", toolCall.ToolName);
        Assert.Contains("dir_path", toolCall.InputJson);
    }

    [Fact]
    public void ParseLine_ToolUseEvent_WithDescription_ExtractsDescription()
    {
        var json = """{"type":"tool_use","tool_name":"run_shell_command","tool_id":"cmd_1","parameters":{"command":"git log","description":"Checking git history"}}""";
        var events = _parser.ParseLine(json);

        var toolCall = Assert.IsType<ToolCallEvent>(events[0]);
        Assert.Equal("Checking git history", toolCall.Description);
    }

    [Fact]
    public void ParseLine_ToolUseEvent_UpdateTopic_ReturnsTextEvent()
    {
        var json = """{"type":"tool_use","tool_name":"update_topic","tool_id":"update_topic__update_topic_123_0","parameters":{"title":"Researching Codebase","summary":"I am exploring the repository structure.","strategic_intent":"Understanding architecture."}}""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var text = Assert.IsType<TextEvent>(events[0]);
        Assert.Equal("I am exploring the repository structure.", text.Text);
    }

    [Fact]
    public void ParseLine_ToolResultEvent_UpdateTopic_ReturnsEmpty()
    {
        var json = """{"type":"tool_result","tool_id":"update_topic__update_topic_123_0","status":"success","output":"Topic updated"}""";
        var events = _parser.ParseLine(json);
        Assert.Empty(events);
    }

    [Fact]
    public void ParseLine_ToolResultEvent_Success_ReturnsToolResultEvent()
    {
        var json = """{"type":"tool_result","timestamp":"2026-05-28T17:13:01.052Z","tool_id":"list_directory__list_directory_123_1","status":"success","output":"file1.ts\nfile2.ts"}""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var result = Assert.IsType<ToolResultEvent>(events[0]);
        Assert.Equal("list_directory__list_directory_123_1", result.ToolUseId);
        Assert.Equal("file1.ts\nfile2.ts", result.Output);
        Assert.False(result.IsError);
    }

    [Fact]
    public void ParseLine_ToolResultEvent_Error_SetsIsError()
    {
        var json = """{"type":"tool_result","tool_id":"cmd_1","status":"error","output":"command not found"}""";
        var events = _parser.ParseLine(json);

        var result = Assert.IsType<ToolResultEvent>(events[0]);
        Assert.True(result.IsError);
    }

    [Fact]
    public void ParseLine_ToolResultEvent_NoOutput_ReturnsNullOutput()
    {
        var json = """{"type":"tool_result","tool_id":"list_directory__list_1","status":"success"}""";
        var events = _parser.ParseLine(json);

        var result = Assert.IsType<ToolResultEvent>(events[0]);
        Assert.Null(result.Output);
        Assert.False(result.IsError);
    }

    [Fact]
    public void ParseLine_ResultEvent_WithStats_ReturnsResultWithUsage()
    {
        var json = """{"type":"result","response":"Done!","session_id":"sess_1","stats":{"models":[{"model":"gemini-2.5-pro","prompt":1000,"candidates":500,"cacheRead":200}]}}""";
        var events = _parser.ParseLine(json);

        Assert.Single(events);
        var result = Assert.IsType<ResultEvent>(events[0]);
        Assert.Equal("Done!", result.Response);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Usage);
        Assert.Equal(1000, result.Usage!.InputTokens);
        Assert.Equal(500, result.Usage.OutputTokens);
        Assert.Equal(200, result.Usage.CacheReadTokens);
    }

    [Fact]
    public void ParseLine_ResultEvent_WithoutStats_HasNullUsage()
    {
        var json = """{"type":"result","response":"Done"}""";
        var events = _parser.ParseLine(json);

        var result = Assert.IsType<ResultEvent>(events[0]);
        Assert.Null(result.Usage);
    }

    [Fact]
    public void ParseLine_UnknownType_ReturnsEmpty()
    {
        var json = """{"type":"heartbeat","ts":"2026-01-01"}""";
        var events = _parser.ParseLine(json);
        Assert.Empty(events);
    }

    [Fact]
    public void Flush_ReturnsEmpty()
    {
        _parser.ParseLine("""{"type":"message","role":"assistant","content":"text"}""");
        var events = _parser.Flush();
        Assert.Empty(events);
    }

    [Fact]
    public void BuildResult_WithExistingResultEvent_StampsExitCode()
    {
        var events = new List<AgentEvent>
        {
            new TextEvent { Kind = AgentEventKind.Text, Text = "hi" },
            new ResultEvent { Kind = AgentEventKind.Result, IsSuccess = true, Response = "done" },
        };

        var result = _parser.BuildResult(events, 0);
        Assert.NotNull(result);
        Assert.Equal(0, result!.ExitCode);
        Assert.True(result.IsSuccess);
        Assert.Equal("done", result.Response);
    }

    [Fact]
    public void BuildResult_WithoutResultEvent_CreatesNewFromExitCode()
    {
        var events = new List<AgentEvent>
        {
            new TextEvent { Kind = AgentEventKind.Text, Text = "hi" },
        };

        var result = _parser.BuildResult(events, 1);
        Assert.NotNull(result);
        Assert.Equal(1, result!.ExitCode);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Reset_IsCallable()
    {
        _parser.Reset();
    }
}
