namespace Ivy.Tendril.Agents.Abstractions;

public interface IAgentSession : IAsyncDisposable
{
    string SessionId { get; }
    string AgentId { get; }
    SessionState State { get; }
    DateTimeOffset StartedAt { get; }
    DateTimeOffset? CompletedAt { get; }
    SessionMetadata? Metadata { get; }
    IObservable<AgentEvent> Events { get; }
    IObservable<string>? RawOutput { get; }
    IObservable<string>? RawStderr { get; }
    ResultEvent? Result { get; }
    Task<ResultEvent> WaitForCompletionAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
    Task KillAsync();
    Task RespondToPermissionAsync(string requestId, PermissionDecision decision, CancellationToken ct = default);
    Task RespondToQuestionAsync(string questionId, QuestionResponse response, CancellationToken ct = default);
    Task SendFollowUpAsync(string message, CancellationToken ct = default);
    bool SupportsPermissionResponse { get; }
    bool SupportsQuestionResponse { get; }
    bool SupportsMultiTurn { get; }
}
