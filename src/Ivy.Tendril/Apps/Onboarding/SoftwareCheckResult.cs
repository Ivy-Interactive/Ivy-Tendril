using Ivy.Tendril.Models;

namespace Ivy.Tendril.Apps.Onboarding;

public record SoftwareCheckResult(
    string DisplayName,
    string Key,
    bool IsInstalled,
    HealthCheckStatus? HealthStatus,
    string InstallUrl,
    bool IsRequired);
