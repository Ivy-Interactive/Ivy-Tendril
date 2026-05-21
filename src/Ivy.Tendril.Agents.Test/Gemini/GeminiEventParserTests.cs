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
    public void ParseLine_SingleLine_ReturnsEmpty()
    {
        var events = _parser.ParseLine("""{"response":"hello"}""");
        Assert.Empty(events);
    }

    [Fact]
    public void ParseLine_MultipleLines_AllBuffered_StillEmpty()
    {
        _parser.ParseLine("{\"session_id\":\"abc\",");
        _parser.ParseLine("\"response\":\"hello\"}");
        var events = _parser.ParseLine("extra line");

        Assert.Empty(events);
    }

    [Fact]
    public void Flush_EmptyBuffer_ReturnsEmpty()
    {
        var events = _parser.Flush();
        Assert.Empty(events);
    }

    [Fact]
    public void Flush_EmptyString_ReturnsEmpty()
    {
        _parser.ParseLine("");
        _parser.ParseLine("   ");
        var events = _parser.Flush();
        Assert.Empty(events);
    }

    [Fact]
    public void Flush_ValidJson_WithResponse_ReturnsSessionInitTextAndResult()
    {
        var json = """
        {
            "session_id": "ses-123",
            "response": "The answer is 42",
            "stats": {
                "models": {
                    "gemini-2.5-pro": {
                        "tokens": {
                            "input": 100,
                            "candidates": 50,
                            "cached": 20,
                            "thoughts": 10
                        },
                        "api": {
                            "totalLatencyMs": 2500
                        }
                    }
                }
            }
        }
        """;

        _parser.ParseLine(json);
        var events = _parser.Flush();

        Assert.Equal(3, events.Count);

        var init = Assert.IsType<SessionInitEvent>(events[0]);
        Assert.Equal(AgentEventKind.SessionInit, init.Kind);
        Assert.Equal("ses-123", init.SessionId);
        Assert.Equal("gemini-2.5-pro", init.Model);

        var text = Assert.IsType<TextEvent>(events[1]);
        Assert.Equal(AgentEventKind.Text, text.Kind);
        Assert.Equal("The answer is 42", text.Text);

        var result = Assert.IsType<ResultEvent>(events[2]);
        Assert.Equal(AgentEventKind.Result, result.Kind);
        Assert.True(result.IsSuccess);
        Assert.Equal("The answer is 42", result.Response);
        Assert.NotNull(result.Usage);
        Assert.Equal(100, result.Usage.InputTokens);
        Assert.Equal(50, result.Usage.OutputTokens);
        Assert.Equal(20, result.Usage.CacheReadTokens);
        Assert.Equal(10, result.Usage.ReasoningTokens);
        Assert.NotNull(result.Duration);
        Assert.Equal(2500, result.Duration.Value.TotalMilliseconds);
    }

    [Fact]
    public void Flush_ValidJson_NoResponse_NoTextEvent_ResultNotSuccess()
    {
        var json = """
        {
            "session_id": "ses-456",
            "stats": {
                "models": {
                    "gemini-2.5-flash": {
                        "tokens": { "input": 50, "candidates": 0 },
                        "api": { "totalLatencyMs": 100 }
                    }
                }
            }
        }
        """;

        _parser.ParseLine(json);
        var events = _parser.Flush();

        Assert.Equal(2, events.Count);

        Assert.IsType<SessionInitEvent>(events[0]);

        var result = Assert.IsType<ResultEvent>(events[1]);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Response);
    }

    [Fact]
    public void Flush_ExtractsSessionId()
    {
        _parser.ParseLine("""{"session_id":"my-session","response":"ok","stats":{"models":{}}}""");
        var events = _parser.Flush();

        var init = Assert.IsType<SessionInitEvent>(events[0]);
        Assert.Equal("my-session", init.SessionId);
    }

    [Fact]
    public void Flush_NoSessionId_DefaultsToEmpty()
    {
        _parser.ParseLine("""{"response":"ok","stats":{"models":{}}}""");
        var events = _parser.Flush();

        var init = Assert.IsType<SessionInitEvent>(events[0]);
        Assert.Equal("", init.SessionId);
    }

    [Fact]
    public void Flush_ExtractsModelFromStats()
    {
        _parser.ParseLine("""{"stats":{"models":{"gemini-2.5-pro-preview":{"tokens":{"input":10,"candidates":5}}}}}""");
        var events = _parser.Flush();

        var init = Assert.IsType<SessionInitEvent>(events[0]);
        Assert.Equal("gemini-2.5-pro-preview", init.Model);
    }

    [Fact]
    public void Flush_ExtractsTokens()
    {
        _parser.ParseLine("""{"response":"x","stats":{"models":{"m":{"tokens":{"input":200,"candidates":100,"cached":50,"thoughts":30}}}}}""");
        var events = _parser.Flush();

        var result = Assert.IsType<ResultEvent>(events[2]);
        Assert.Equal(200, result.Usage!.InputTokens);
        Assert.Equal(100, result.Usage.OutputTokens);
        Assert.Equal(50, result.Usage.CacheReadTokens);
        Assert.Equal(30, result.Usage.ReasoningTokens);
    }

    [Fact]
    public void Flush_ExtractsTotalLatencyMs()
    {
        _parser.ParseLine("""{"response":"x","stats":{"models":{"m":{"tokens":{"input":1,"candidates":1},"api":{"totalLatencyMs":4200}}}}}""");
        var events = _parser.Flush();

        var result = Assert.IsType<ResultEvent>(events[2]);
        Assert.NotNull(result.Duration);
        Assert.Equal(4200, result.Duration.Value.TotalMilliseconds);
    }

    [Fact]
    public void Flush_ZeroLatency_DurationIsNull()
    {
        _parser.ParseLine("""{"response":"x","stats":{"models":{"m":{"tokens":{"input":1,"candidates":1},"api":{"totalLatencyMs":0}}}}}""");
        var events = _parser.Flush();

        var result = Assert.IsType<ResultEvent>(events[2]);
        Assert.Null(result.Duration);
    }

    [Fact]
    public void Flush_MalformedJson_ReturnsUnknownEvent()
    {
        _parser.ParseLine("{not valid json at all!!");
        var events = _parser.Flush();

        Assert.Single(events);
        var unknown = Assert.IsType<UnknownEvent>(events[0]);
        Assert.Equal(AgentEventKind.Unknown, unknown.Kind);
        Assert.Contains("not valid json", unknown.Content);
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
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void BuildResult_WithNoResultEvent_CreatesSyntheticSuccess()
    {
        var events = new List<AgentEvent>
        {
            new TextEvent { Kind = AgentEventKind.Text, Text = "hello" }
        };

        var result = _parser.BuildResult(events, 0);

        Assert.NotNull(result);
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void BuildResult_WithNoResultEvent_NonZeroExit_IsNotSuccess()
    {
        var result = _parser.BuildResult([], 1);

        Assert.NotNull(result);
        Assert.Equal(1, result.ExitCode);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void BuildResult_WithMultipleEvents_UsesLastResultEvent()
    {
        var events = new List<AgentEvent>
        {
            new TextEvent { Kind = AgentEventKind.Text, Text = "first" },
            new ResultEvent { Kind = AgentEventKind.Result, IsSuccess = false, Response = "err" },
            new ResultEvent { Kind = AgentEventKind.Result, IsSuccess = true, Response = "final" },
        };

        var result = _parser.BuildResult(events, 0);

        Assert.NotNull(result);
        Assert.Equal("final", result.Response);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Reset_ClearsBuffer_FlushReturnsEmpty()
    {
        _parser.ParseLine("""{"response":"buffered data"}""");
        _parser.Reset();

        var events = _parser.Flush();
        Assert.Empty(events);
    }
}
