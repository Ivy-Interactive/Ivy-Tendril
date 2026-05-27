namespace Ivy.Tendril.Agents.Abstractions;

public sealed record PermissionDecision
{
    public required bool Granted { get; init; }
    public PermissionScope Scope { get; init; } = PermissionScope.Once;
    public string? UpdatedInput { get; init; }
    public ResponseSource Source { get; init; } = ResponseSource.Automation;
}

public sealed record QuestionResponse
{
    public IReadOnlyList<string>? Answers { get; init; }
    public bool IsCancelled { get; init; }
    public ResponseSource Source { get; init; } = ResponseSource.Automation;
}
