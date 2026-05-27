namespace Ivy.Tendril.Agents.Abstractions;

public interface IInteractionHandler
{
    Task<PermissionDecision?> HandlePermissionAsync(
        PermissionRequestEvent request,
        InteractionContext context,
        CancellationToken ct = default);

    Task<QuestionResponse?> HandleQuestionAsync(
        UserQuestionEvent question,
        InteractionContext context,
        CancellationToken ct = default);
}

public sealed record InteractionContext
{
    public required string SessionId { get; init; }
    public required string AgentId { get; init; }
    public SessionMetadata? Metadata { get; init; }
    public IReadOnlySet<string> SessionApprovedPatterns { get; init; } = new HashSet<string>();
}

public sealed class AutoApproveHandler : IInteractionHandler
{
    public static readonly AutoApproveHandler Instance = new();

    public Task<PermissionDecision?> HandlePermissionAsync(
        PermissionRequestEvent request, InteractionContext context, CancellationToken ct = default)
        => Task.FromResult<PermissionDecision?>(new PermissionDecision
        {
            Granted = true,
            Scope = PermissionScope.Session,
            Source = ResponseSource.Automation,
        });

    public Task<QuestionResponse?> HandleQuestionAsync(
        UserQuestionEvent question, InteractionContext context, CancellationToken ct = default)
        => Task.FromResult<QuestionResponse?>(null);
}

public sealed class PassthroughHandler : IInteractionHandler
{
    public static readonly PassthroughHandler Instance = new();

    public Task<PermissionDecision?> HandlePermissionAsync(
        PermissionRequestEvent request, InteractionContext context, CancellationToken ct = default)
        => Task.FromResult<PermissionDecision?>(null);

    public Task<QuestionResponse?> HandleQuestionAsync(
        UserQuestionEvent question, InteractionContext context, CancellationToken ct = default)
        => Task.FromResult<QuestionResponse?>(null);
}
