namespace Ivy.Tendril.Agents.Abstractions;

public sealed record ValidationProblem
{
    public required ValidationSeverity Severity { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? PropertyName { get; init; }
}

public enum ValidationSeverity
{
    Error,
    Warning,
    Info
}
