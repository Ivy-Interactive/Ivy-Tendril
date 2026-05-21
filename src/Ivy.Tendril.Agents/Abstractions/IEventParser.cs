namespace Ivy.Tendril.Agents.Abstractions;

public interface IEventParser
{
    string AgentId { get; }
    IReadOnlyList<AgentEvent> ParseLine(string rawLine);
    IReadOnlyList<AgentEvent> Flush();
    ResultEvent? BuildResult(IReadOnlyList<AgentEvent> events, int exitCode);
    void Reset();
}

public interface IFailureAnalyzer
{
    FailureAnalysis Analyze(FailureContext context);
}

public sealed record FailureContext
{
    public required IReadOnlyList<AgentEvent> Events { get; init; }
    public IReadOnlyList<string> StderrLines { get; init; } = [];
    public int? ExitCode { get; init; }
    public bool TimedOut { get; init; }
    public bool IdleTimeout { get; init; }
    public required string AgentId { get; init; }
}

public sealed record FailureAnalysis
{
    public required FailureKind Kind { get; init; }
    public required string Reason { get; init; }
    public IReadOnlyList<string> ContextLines { get; init; } = [];
    public bool IsRetryable { get; init; }
    public string? Suggestion { get; init; }
}

public enum FailureKind
{
    RateLimit,
    AuthError,
    InvalidModel,
    Timeout,
    IdleTimeout,
    ProcessCrash,
    ValidationError,
    PermissionBlocked,
    NetworkError,
    Unknown
}
