namespace Ivy.Tendril.Agents.Abstractions;

public interface IAgentHealthCheck
{
    string AgentId { get; }
    Task<AgentInstallStatus> CheckInstallAsync(CancellationToken ct = default);
    Task<AgentAuthResult> CheckAuthAsync(CancellationToken ct = default);
    Task<string?> GetVersionAsync(CancellationToken ct = default);
    Task<ModelValidationResult> ValidateModelAsync(string model, CancellationToken ct = default);
    Task<bool> RunAuthFlowAsync(AuthFlowCallbacks callbacks, CancellationToken ct = default);
    AgentOnboardingInfo GetOnboardingInfo();
}

public sealed record AuthFlowCallbacks
{
    public required Func<string, Task> OnUrl { get; init; }
    public Action<string>? OnCode { get; init; }
}

public sealed record AgentInstallStatus
{
    public required bool IsInstalled { get; init; }
    public string? Version { get; init; }
    public string? BinaryPath { get; init; }
    public string? Error { get; init; }
}

public sealed record AgentAuthResult
{
    public required AuthStatus Status { get; init; }
    public string? AuthMethod { get; init; }
    public string? Provider { get; init; }
    public string? Error { get; init; }
    public string? SignInHint { get; init; }
}

public sealed record ModelValidationResult
{
    public required ModelValidationStatus Status { get; init; }
    public string? Model { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed record AgentOnboardingInfo
{
    public required string DisplayName { get; init; }
    public required string InstallCommand { get; init; }
    public string? InstallUrl { get; init; }
    public string? AuthCommand { get; init; }
    public string? SignInHint { get; init; }
    public string? DocsUrl { get; init; }
}
