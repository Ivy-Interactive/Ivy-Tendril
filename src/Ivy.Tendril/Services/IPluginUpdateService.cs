namespace Ivy.Tendril.Services;

public interface IPluginUpdateService
{
    /// <summary>
    /// Checks for available updates across all NuGet-installed plugins.
    /// Results are cached for 15 minutes unless forceRefresh is true.
    /// </summary>
    Task<IReadOnlyList<PluginUpdateInfo>> CheckForUpdatesAsync(bool forceRefresh = false);

    /// <summary>
    /// Downloads, verifies, and applies an update for a single plugin.
    /// Orchestrates: download nupkg → verify hash → extract to temp → resolve deps → unload → move → reload.
    /// </summary>
    Task UpdatePluginAsync(string packageId, IProgress<int>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates all plugins that have available updates, sequentially.
    /// </summary>
    Task UpdateAllAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fired when the update check state changes (e.g., new updates found or an update was applied).
    /// </summary>
    event Action? UpdateStateChanged;
}

public record PluginUpdateInfo(
    string PackageId,
    string InstalledVersion,
    string LatestVersion,
    string LatestHash,
    bool HasUpdate,
    DateTime? LastChecked);
