using System.Text.Json;

namespace Ivy.Tendril.Agents.Abstractions;

public interface IEventSerializer
{
    string Serialize(AgentEvent evt);
    AgentEvent? Deserialize(string json);
}

public abstract record EventWire
{
    public abstract string Kind { get; }
    public required string Timestamp { get; init; }
}

public sealed record SessionInitWire : EventWire
{
    public override string Kind => "session_init";
    public required string SessionId { get; init; }
    public string? Model { get; init; }
    public IReadOnlyList<string>? Tools { get; init; }
}

public sealed record TextWire : EventWire
{
    public override string Kind => "text";
    public required string Text { get; init; }
    public bool Delta { get; init; }
}

public sealed record ThinkingWire : EventWire
{
    public override string Kind => "thinking";
    public required string Content { get; init; }
}

public sealed record ToolCallWire : EventWire
{
    public override string Kind => "tool_call";
    public required string ToolUseId { get; init; }
    public required string ToolName { get; init; }
    public string? Description { get; init; }
    public JsonElement? Input { get; init; }
}

public sealed record ToolResultWire : EventWire
{
    public override string Kind => "tool_result";
    public required string ToolUseId { get; init; }
    public string? ToolName { get; init; }
    public string? Output { get; init; }
    public bool IsError { get; init; }
}

public sealed record PermissionRequestWire : EventWire
{
    public override string Kind => "permission_request";
    public required string RequestId { get; init; }
    public required string ToolName { get; init; }
    public string? Description { get; init; }
    public string? Input { get; init; }
    public bool IsDestructive { get; init; }
    public string? Pattern { get; init; }
}

public sealed record PermissionDenialWire : EventWire
{
    public override string Kind => "permission_denial";
    public required string ToolName { get; init; }
    public string? InputSummary { get; init; }
}

public sealed record ErrorWire : EventWire
{
    public override string Kind => "error";
    public required string Message { get; init; }
    public string? Code { get; init; }
    public bool IsRetryable { get; init; }
    public bool IsAuthError { get; init; }
}

public sealed record ResultWire : EventWire
{
    public override string Kind => "result";
    public string? Response { get; init; }
    public bool IsSuccess { get; init; }
    public long? DurationMs { get; init; }
    public int? TurnCount { get; init; }
    public UsageWire? Usage { get; init; }
    public int? ExitCode { get; init; }
    public IReadOnlyList<PermissionDenialWire>? PermissionDenials { get; init; }
}

public sealed record FileChangeWire : EventWire
{
    public override string Kind => "file_change";
    public required string FilePath { get; init; }
    public required string ChangeKind { get; init; }
    public int LinesAdded { get; init; }
    public int LinesRemoved { get; init; }
}

public sealed record UserQuestionWire : EventWire
{
    public override string Kind => "user_question";
    public required string QuestionId { get; init; }
    public required string Question { get; init; }
    public IReadOnlyList<QuestionOptionWire>? Options { get; init; }
    public bool MultiSelect { get; init; }
    public string? Description { get; init; }
    public bool IsBlocking { get; init; }
    public long? TimeoutMs { get; init; }
}

public sealed record QuestionOptionWire
{
    public required string Label { get; init; }
    public required string Value { get; init; }
    public string? Description { get; init; }
}

public sealed record UsageWire
{
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int CacheReadTokens { get; init; }
    public int CacheWriteTokens { get; init; }
    public int ReasoningTokens { get; init; }
    public decimal? CostUsd { get; init; }
    public int? PremiumRequests { get; init; }
    public string? Model { get; init; }
    public IReadOnlyList<UsageWire>? ModelBreakdown { get; init; }
}
