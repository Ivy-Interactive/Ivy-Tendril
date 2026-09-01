namespace Ivy.Tendril.Services;

public interface IVersionCheckService
{
    Task<VersionInfo> CheckForUpdatesAsync(bool forceRefresh = false);

    VersionInfo? CachedVersionInfo { get; }

    bool CanSelfUpdate { get; }

    Task StartUpdateAsync(UpdateProgress progress, CancellationToken cancellationToken = default);
}

public record VersionInfo(
    string CurrentVersion,
    string? LatestVersion,
    bool HasUpdate,
    DateTime? LastChecked);

public record UpdateProgress(
    Action<int> OnProgress,
    Action<string> OnStatus);
