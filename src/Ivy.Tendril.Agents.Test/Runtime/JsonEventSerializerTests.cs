using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Runtime;

namespace Ivy.Tendril.Agents.Test.Runtime;

public class JsonEventSerializerTests
{
    private readonly JsonEventSerializer _serializer = new();

    [Fact]
    public void Serialize_TextEvent_ProducesValidJson()
    {
        var evt = new TextEvent
        {
            Kind = AgentEventKind.Text,
            Text = "Hello, world!",
            IsDelta = true,
        };

        var json = _serializer.Serialize(evt);

        Assert.Contains("\"kind\":\"text\"", json);
        Assert.Contains("\"text\":\"Hello, world!\"", json);
        Assert.Contains("\"delta\":true", json);
    }

    [Fact]
    public void Serialize_ThinkingEvent_ProducesValidJson()
    {
        var evt = new ThinkingEvent
        {
            Kind = AgentEventKind.Thinking,
            Content = "Let me think...",
        };

        var json = _serializer.Serialize(evt);

        Assert.Contains("\"kind\":\"thinking\"", json);
        Assert.Contains("\"content\":\"Let me think...\"", json);
    }

    [Fact]
    public void Serialize_ToolCallEvent_ProducesValidJson()
    {
        var evt = new ToolCallEvent
        {
            Kind = AgentEventKind.ToolCall,
            ToolUseId = "toolu_123",
            ToolName = "Read",
            InputJson = "{\"file_path\":\"/tmp/test.txt\"}",
        };

        var json = _serializer.Serialize(evt);

        Assert.Contains("\"kind\":\"tool_call\"", json);
        Assert.Contains("\"tool_use_id\":\"toolu_123\"", json);
        Assert.Contains("\"tool_name\":\"Read\"", json);
    }

    [Fact]
    public void Serialize_ToolResultEvent_ProducesValidJson()
    {
        var evt = new ToolResultEvent
        {
            Kind = AgentEventKind.ToolResult,
            ToolUseId = "toolu_123",
            ToolName = "Read",
            Output = "file contents here",
            IsError = false,
        };

        var json = _serializer.Serialize(evt);

        Assert.Contains("\"kind\":\"tool_result\"", json);
        Assert.Contains("\"output\":\"file contents here\"", json);
    }

    [Fact]
    public void Serialize_ErrorEvent_ProducesValidJson()
    {
        var evt = new ErrorEvent
        {
            Kind = AgentEventKind.Error,
            Message = "Something went wrong",
            Code = "E001",
            IsRetryable = true,
            IsAuthError = false,
        };

        var json = _serializer.Serialize(evt);

        Assert.Contains("\"kind\":\"error\"", json);
        Assert.Contains("\"message\":\"Something went wrong\"", json);
        Assert.Contains("\"is_retryable\":true", json);
    }

    [Fact]
    public void Serialize_ResultEvent_ProducesValidJson()
    {
        var evt = new ResultEvent
        {
            Kind = AgentEventKind.Result,
            Response = "Done",
            IsSuccess = true,
            Duration = TimeSpan.FromSeconds(5),
            TurnCount = 3,
            ExitCode = 0,
            Usage = new AgentUsage
            {
                InputTokens = 100,
                OutputTokens = 50,
                CostUsd = 0.01m,
            },
        };

        var json = _serializer.Serialize(evt);

        Assert.Contains("\"kind\":\"result\"", json);
        Assert.Contains("\"response\":\"Done\"", json);
        Assert.Contains("\"is_success\":true", json);
        Assert.Contains("\"duration_ms\":5000", json);
        Assert.Contains("\"turn_count\":3", json);
    }

    [Fact]
    public void Serialize_SessionInitEvent_ProducesValidJson()
    {
        var evt = new SessionInitEvent
        {
            Kind = AgentEventKind.SessionInit,
            SessionId = "sess_abc",
            Model = "claude-sonnet-4-5-20250514",
            AvailableTools = ["Read", "Write", "Bash"],
        };

        var json = _serializer.Serialize(evt);

        Assert.Contains("\"kind\":\"session_init\"", json);
        Assert.Contains("\"session_id\":\"sess_abc\"", json);
        Assert.Contains("\"model\":\"claude-sonnet-4-5-20250514\"", json);
    }

    [Fact]
    public void Serialize_PermissionRequestEvent_ProducesValidJson()
    {
        var evt = new PermissionRequestEvent
        {
            Kind = AgentEventKind.PermissionRequest,
            RequestId = "req_1",
            ToolName = "Bash",
            Description = "Run a shell command",
            IsDestructive = true,
        };

        var json = _serializer.Serialize(evt);

        Assert.Contains("\"kind\":\"permission_request\"", json);
        Assert.Contains("\"is_destructive\":true", json);
    }

    [Fact]
    public void Serialize_FileChangeEvent_ProducesValidJson()
    {
        var evt = new FileChangeEvent
        {
            Kind = AgentEventKind.FileChange,
            FilePath = "/tmp/test.txt",
            ChangeKind = FileChangeKind.Created,
            LinesAdded = 10,
            LinesRemoved = 0,
        };

        var json = _serializer.Serialize(evt);

        Assert.Contains("\"kind\":\"file_change\"", json);
        Assert.Contains("\"change_kind\":\"created\"", json);
        Assert.Contains("\"lines_added\":10", json);
    }

    [Fact]
    public void Serialize_UserQuestionEvent_ProducesValidJson()
    {
        var evt = new UserQuestionEvent
        {
            Kind = AgentEventKind.UserQuestion,
            QuestionId = "q_1",
            Question = "Which file?",
            Options = [new QuestionOption { Label = "A", Value = "a" }],
            IsBlocking = true,
        };

        var json = _serializer.Serialize(evt);

        Assert.Contains("\"kind\":\"user_question\"", json);
        Assert.Contains("\"question\":\"Which file?\"", json);
        Assert.Contains("\"is_blocking\":true", json);
    }

    [Fact]
    public void RoundTrip_TextEvent_PreservesData()
    {
        var original = new TextEvent
        {
            Kind = AgentEventKind.Text,
            Text = "Hello!",
            IsDelta = true,
        };

        var json = _serializer.Serialize(original);
        var deserialized = _serializer.Deserialize(json) as TextEvent;

        Assert.NotNull(deserialized);
        Assert.Equal("Hello!", deserialized.Text);
        Assert.True(deserialized.IsDelta);
    }

    [Fact]
    public void RoundTrip_ToolCallEvent_PreservesData()
    {
        var original = new ToolCallEvent
        {
            Kind = AgentEventKind.ToolCall,
            ToolUseId = "toolu_abc",
            ToolName = "Bash",
            InputJson = "{\"command\":\"ls\"}",
        };

        var json = _serializer.Serialize(original);
        var deserialized = _serializer.Deserialize(json) as ToolCallEvent;

        Assert.NotNull(deserialized);
        Assert.Equal("toolu_abc", deserialized.ToolUseId);
        Assert.Equal("Bash", deserialized.ToolName);
        Assert.Contains("ls", deserialized.InputJson);
    }

    [Fact]
    public void RoundTrip_ResultEvent_PreservesUsage()
    {
        var original = new ResultEvent
        {
            Kind = AgentEventKind.Result,
            Response = "Done",
            IsSuccess = true,
            TurnCount = 2,
            ExitCode = 0,
            Duration = TimeSpan.FromMilliseconds(1500),
            Usage = new AgentUsage
            {
                InputTokens = 500,
                OutputTokens = 200,
                CacheReadTokens = 100,
                CacheWriteTokens = 50,
                CostUsd = 0.003m,
                Model = "claude-sonnet-4-5-20250514",
            },
        };

        var json = _serializer.Serialize(original);
        var deserialized = _serializer.Deserialize(json) as ResultEvent;

        Assert.NotNull(deserialized);
        Assert.True(deserialized.IsSuccess);
        Assert.Equal(2, deserialized.TurnCount);
        Assert.NotNull(deserialized.Usage);
        Assert.Equal(500, deserialized.Usage.InputTokens);
        Assert.Equal(200, deserialized.Usage.OutputTokens);
        Assert.Equal(0.003m, deserialized.Usage.CostUsd);
    }

    [Fact]
    public void RoundTrip_ErrorEvent_PreservesData()
    {
        var original = new ErrorEvent
        {
            Kind = AgentEventKind.Error,
            Message = "Rate limit",
            Code = "429",
            IsRetryable = true,
            IsAuthError = false,
        };

        var json = _serializer.Serialize(original);
        var deserialized = _serializer.Deserialize(json) as ErrorEvent;

        Assert.NotNull(deserialized);
        Assert.Equal("Rate limit", deserialized.Message);
        Assert.Equal("429", deserialized.Code);
        Assert.True(deserialized.IsRetryable);
        Assert.False(deserialized.IsAuthError);
    }

    [Fact]
    public void RoundTrip_FileChangeEvent_PreservesData()
    {
        var original = new FileChangeEvent
        {
            Kind = AgentEventKind.FileChange,
            FilePath = "/src/main.cs",
            ChangeKind = FileChangeKind.Modified,
            LinesAdded = 5,
            LinesRemoved = 2,
        };

        var json = _serializer.Serialize(original);
        var deserialized = _serializer.Deserialize(json) as FileChangeEvent;

        Assert.NotNull(deserialized);
        Assert.Equal("/src/main.cs", deserialized.FilePath);
        Assert.Equal(FileChangeKind.Modified, deserialized.ChangeKind);
        Assert.Equal(5, deserialized.LinesAdded);
        Assert.Equal(2, deserialized.LinesRemoved);
    }

    [Fact]
    public void Deserialize_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(_serializer.Deserialize(""));
        Assert.Null(_serializer.Deserialize("  "));
        Assert.Null(_serializer.Deserialize(null!));
    }

    [Fact]
    public void Deserialize_InvalidJson_ReturnsNull()
    {
        Assert.Null(_serializer.Deserialize("not json"));
        Assert.Null(_serializer.Deserialize("{incomplete"));
    }

    [Fact]
    public void Deserialize_NoKindProperty_ReturnsNull()
    {
        Assert.Null(_serializer.Deserialize("{\"text\":\"hello\"}"));
    }

    [Fact]
    public void Deserialize_UnknownKind_ReturnsUnknownEvent()
    {
        var result = _serializer.Deserialize("{\"kind\":\"future_event\",\"timestamp\":\"2025-01-01T00:00:00Z\"}");

        Assert.NotNull(result);
        Assert.IsType<UnknownEvent>(result);
    }

    [Fact]
    public void Serialize_ResultEvent_WithPermissionDenials()
    {
        var evt = new ResultEvent
        {
            Kind = AgentEventKind.Result,
            IsSuccess = false,
            PermissionDenials =
            [
                new PermissionDenialEvent
                {
                    Kind = AgentEventKind.PermissionDenial,
                    ToolName = "Bash",
                    InputSummary = "rm -rf /",
                },
            ],
        };

        var json = _serializer.Serialize(evt);

        Assert.Contains("permission_denials", json);
        Assert.Contains("Bash", json);
    }

    [Fact]
    public void Serialize_NullOptionalFields_OmittedFromJson()
    {
        var evt = new ToolResultEvent
        {
            Kind = AgentEventKind.ToolResult,
            ToolUseId = "toolu_1",
        };

        var json = _serializer.Serialize(evt);

        Assert.DoesNotContain("\"output\"", json);
        Assert.DoesNotContain("\"tool_name\"", json);
    }
}
