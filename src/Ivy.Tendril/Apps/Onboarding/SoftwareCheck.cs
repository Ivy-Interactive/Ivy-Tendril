namespace Ivy.Tendril.Apps.Onboarding;

public record SoftwareCheck(
    string Name,
    string Key,
    string InstallUrl,
    bool IsRequired,
    Func<Task<bool>> InstallCheck,
    Func<Task<HealthCheckStatus>>? HealthCheck = null,
    string? HealthHint = null);
