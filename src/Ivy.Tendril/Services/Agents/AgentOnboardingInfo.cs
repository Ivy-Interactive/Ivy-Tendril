using Ivy.Tendril.Models;

namespace Ivy.Tendril.Services.Agents;

public record AgentOnboardingInfo(
    string DisplayName,
    string InstallUrl,
    string VersionArgs,
    Func<Task<HealthCheckStatus>>? HealthCheck = null,
    string? SignInHint = null);
