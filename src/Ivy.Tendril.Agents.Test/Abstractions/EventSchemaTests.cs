using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Test.Abstractions;

public class EventSchemaTests
{
    [Fact]
    public void SessionInitWire_Kind_IsCorrect()
    {
        var wire = new SessionInitWire
        {
            Timestamp = "2026-01-01T00:00:00Z",
            SessionId = "s1",
        };
        Assert.Equal("session_init", wire.Kind);
    }

    [Fact]
    public void TextWire_Kind_IsCorrect()
    {
        var wire = new TextWire { Timestamp = "2026-01-01T00:00:00Z", Text = "hi" };
        Assert.Equal("text", wire.Kind);
    }

    [Fact]
    public void ThinkingWire_Kind_IsCorrect()
    {
        var wire = new ThinkingWire { Timestamp = "2026-01-01T00:00:00Z", Content = "thinking" };
        Assert.Equal("thinking", wire.Kind);
    }

    [Fact]
    public void ToolCallWire_Kind_IsCorrect()
    {
        var wire = new ToolCallWire { Timestamp = "2026-01-01T00:00:00Z", ToolUseId = "t1", ToolName = "Read" };
        Assert.Equal("tool_call", wire.Kind);
    }

    [Fact]
    public void ToolResultWire_Kind_IsCorrect()
    {
        var wire = new ToolResultWire { Timestamp = "2026-01-01T00:00:00Z", ToolUseId = "t1" };
        Assert.Equal("tool_result", wire.Kind);
    }

    [Fact]
    public void PermissionRequestWire_Kind_IsCorrect()
    {
        var wire = new PermissionRequestWire { Timestamp = "2026-01-01T00:00:00Z", RequestId = "r1", ToolName = "Bash" };
        Assert.Equal("permission_request", wire.Kind);
    }

    [Fact]
    public void PermissionDenialWire_Kind_IsCorrect()
    {
        var wire = new PermissionDenialWire { Timestamp = "2026-01-01T00:00:00Z", ToolName = "Write" };
        Assert.Equal("permission_denial", wire.Kind);
    }

    [Fact]
    public void ErrorWire_Kind_IsCorrect()
    {
        var wire = new ErrorWire { Timestamp = "2026-01-01T00:00:00Z", Message = "err" };
        Assert.Equal("error", wire.Kind);
    }

    [Fact]
    public void ResultWire_Kind_IsCorrect()
    {
        var wire = new ResultWire { Timestamp = "2026-01-01T00:00:00Z" };
        Assert.Equal("result", wire.Kind);
    }

    [Fact]
    public void FileChangeWire_Kind_IsCorrect()
    {
        var wire = new FileChangeWire { Timestamp = "2026-01-01T00:00:00Z", FilePath = "/tmp/x", ChangeKind = "created" };
        Assert.Equal("file_change", wire.Kind);
    }

    [Fact]
    public void UserQuestionWire_Kind_IsCorrect()
    {
        var wire = new UserQuestionWire { Timestamp = "2026-01-01T00:00:00Z", QuestionId = "q1", Question = "?" };
        Assert.Equal("user_question", wire.Kind);
    }

    [Fact]
    public void UsageWire_AllFields_RoundTrip()
    {
        var wire = new UsageWire
        {
            InputTokens = 100,
            OutputTokens = 50,
            CacheReadTokens = 20,
            CacheWriteTokens = 10,
            ReasoningTokens = 5,
            CostUsd = 0.01m,
            PremiumRequests = 1,
            Model = "opus",
        };

        Assert.Equal(100, wire.InputTokens);
        Assert.Equal(0.01m, wire.CostUsd);
    }

    [Fact]
    public void ResultWire_WithUsageAndDenials()
    {
        var wire = new ResultWire
        {
            Timestamp = "2026-01-01T00:00:00Z",
            Response = "done",
            IsSuccess = true,
            DurationMs = 5000,
            TurnCount = 3,
            ExitCode = 0,
            Usage = new UsageWire { InputTokens = 100, OutputTokens = 50 },
            PermissionDenials = [new PermissionDenialWire { Timestamp = "2026-01-01T00:00:00Z", ToolName = "Bash" }],
        };

        Assert.Equal("done", wire.Response);
        Assert.True(wire.IsSuccess);
        Assert.NotNull(wire.Usage);
        Assert.Single(wire.PermissionDenials!);
    }

    [Fact]
    public void QuestionOptionWire_CreatesCorrectly()
    {
        var opt = new QuestionOptionWire
        {
            Label = "Option A",
            Value = "a",
            Description = "First option",
        };

        Assert.Equal("Option A", opt.Label);
        Assert.Equal("a", opt.Value);
    }
}
