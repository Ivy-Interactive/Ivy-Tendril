namespace Ivy.Tendril.Agents.Abstractions;

public abstract record AgentEvent
{
    public required AgentEventKind Kind { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string? RawLine { get; init; }
}

public enum AgentEventKind
{
    SessionInit,
    SessionStarting,
    SessionActive,
    SessionCompleted,
    IdleTimeout,
    Retry,
    Text,
    Thinking,
    ToolCall,
    ToolResult,
    PermissionRequest,
    PermissionDenial,
    UserQuestion,
    Error,
    Result,
    FileChange,
    Stderr,
    System,
    Unknown
}

public sealed record SessionInitEvent : AgentEvent
{
    public required string SessionId { get; init; }
    public string? Model { get; init; }
    public IReadOnlyList<string>? AvailableTools { get; init; }
}

public sealed record SessionStartingEvent : AgentEvent
{
    public required string SessionId { get; init; }
    public required string AgentId { get; init; }
    public required AgentLaunchConfig Config { get; init; }
    public required TransportKind Transport { get; init; }
    public SessionMetadata? Metadata { get; init; }
}

public sealed record SessionActiveEvent : AgentEvent
{
    public required string SessionId { get; init; }
    public required AgentEvent FirstEvent { get; init; }
}

public sealed record SessionCompletedEvent : AgentEvent
{
    public required string SessionId { get; init; }
    public required string AgentId { get; init; }
    public required SessionState FinalState { get; init; }
    public required ResultEvent Result { get; init; }
    public TimeSpan WallClockDuration { get; init; }
    public int RetryCount { get; init; }
    public SessionMetadata? Metadata { get; init; }
}

public sealed record IdleTimeoutEvent : AgentEvent
{
    public required string SessionId { get; init; }
    public required TimeSpan IdleDuration { get; init; }
}

public sealed record RetryEvent : AgentEvent
{
    public required string SessionId { get; init; }
    public required ErrorEvent Error { get; init; }
    public required int Attempt { get; init; }
    public string? FallbackAgentId { get; init; }
}

public sealed record TextEvent : AgentEvent
{
    public required string Text { get; init; }
    public bool IsDelta { get; init; }
}

public sealed record ThinkingEvent : AgentEvent
{
    public required string Content { get; init; }
}

public sealed record ToolCallEvent : AgentEvent
{
    public required string ToolUseId { get; init; }
    public required string ToolName { get; init; }
    public string? InputJson { get; init; }
    public string? Description { get; init; }
}

public sealed record ToolResultEvent : AgentEvent
{
    public required string ToolUseId { get; init; }
    public string? ToolName { get; init; }
    public string? Output { get; init; }
    public bool IsError { get; init; }
}

public sealed record PermissionRequestEvent : AgentEvent
{
    public required string RequestId { get; init; }
    public required string ToolName { get; init; }
    public string? Description { get; init; }
    public string? Input { get; init; }
    public bool IsDestructive { get; init; }
    public string? Pattern { get; init; }
}

public sealed record PermissionDenialEvent : AgentEvent
{
    public required string ToolName { get; init; }
    public string? InputSummary { get; init; }
}

public sealed record UserQuestionEvent : AgentEvent
{
    public required string QuestionId { get; init; }
    public required string Question { get; init; }
    public IReadOnlyList<QuestionOption>? Options { get; init; }
    public bool AllowMultiSelect { get; init; }
    public string? Description { get; init; }
    public bool IsBlocking { get; init; }
    public TimeSpan? Timeout { get; init; }
}

public sealed record QuestionOption
{
    public required string Label { get; init; }
    public required string Value { get; init; }
    public string? Description { get; init; }
}

public sealed record ErrorEvent : AgentEvent
{
    public required string Message { get; init; }
    public string? Code { get; init; }
    public bool IsRetryable { get; init; }
    public bool IsAuthError { get; init; }
}

public sealed record ResultEvent : AgentEvent
{
    public string? Response { get; init; }
    public AgentUsage? Usage { get; init; }
    public TimeSpan? Duration { get; init; }
    public int? TurnCount { get; init; }
    public IReadOnlyList<PermissionDenialEvent> PermissionDenials { get; init; } = [];
    public bool IsSuccess { get; init; }
    public int? ExitCode { get; init; }
}

public sealed record StderrEvent : AgentEvent
{
    public required string Text { get; init; }
}

public sealed record FileChangeEvent : AgentEvent
{
    public required string FilePath { get; init; }
    public required FileChangeKind ChangeKind { get; init; }
    public int LinesAdded { get; init; }
    public int LinesRemoved { get; init; }
}

public enum FileChangeKind
{
    Created,
    Modified,
    Deleted
}

public sealed record SystemEvent : AgentEvent
{
    public required string Subtype { get; init; }
    public string? Message { get; init; }
}

public sealed record UnknownEvent : AgentEvent
{
    public required string Content { get; init; }
}
