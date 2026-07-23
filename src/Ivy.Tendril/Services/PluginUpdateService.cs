using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Ivy.Core.Plugins;
using Ivy.Plugins;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services;

internal class PluginUpdateService(
    IHttpClientFactory httpClientFactory,
    IPluginManager pluginManager,
    IConfigService configService,
    TendrilArgs tendrilArgs,
    PluginUninstallService uninstallService,
    NuGetDependencyResolver dependencyResolver,
    ILogger<PluginUpdateService> logger) : IPluginUpdateService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);

    private IReadOnlyList<PluginUpdateInfo>? _cachedResult;
    private DateTime _lastCheckTime = DateTime.MinValue;
    private readonly SemaphoreSlim _updateLock = new(1, 1);

    public event Action? UpdateStateChanged;

    private record RegistryPlugin(string PackageId, string Version, string Hash, string Title,
        string? Description, string? IconUrl, string? IconKind, string? IconValue, string? ProjectUrl);

    public async Task<IReadOnlyList<PluginUpdateInfo>> CheckForUpdatesAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && _cachedResult != null && DateTime.UtcNow - _lastCheckTime < CacheDuration)
            return _cachedResult;

        var now = DateTime.UtcNow;

        try
        {
            using var http = httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(15);

            var registryPlugins = await http.GetFromJsonAsync<RegistryPlugin[]>(
                $"{tendrilArgs.ServicesUrl}/plugins");

            if (registryPlugins == null)
            {
                _cachedResult ??= [];
                return _cachedResult;
            }

            var registryLookup = registryPlugins.ToDictionary(
                p => p.PackageId, p => p, StringComparer.OrdinalIgnoreCase);

            var updates = new List<PluginUpdateInfo>();
            var pluginsDir = Path.Combine(configService.TendrilHome, "plugins");

            // Check active plugins
            foreach (var id in pluginManager.GetActivePluginIds())
            {
                var updateInfo = CheckPluginForUpdate(id, pluginsDir, registryLookup, now);
                if (updateInfo != null)
                    updates.Add(updateInfo);
            }

            // Check unconfigured plugins (they're still installed, just missing config)
            foreach (var plugin in pluginManager.GetUnconfiguredPlugins())
            {
                var updateInfo = CheckPluginForUpdate(plugin.Id, pluginsDir, registryLookup, now);
                if (updateInfo != null)
                    updates.Add(updateInfo);
            }

            var previousHadUpdates = _cachedResult?.Any(u => u.HasUpdate) == true;
            _cachedResult = updates;
            _lastCheckTime = now;

            if (updates.Any(u => u.HasUpdate) != previousHadUpdates)
                UpdateStateChanged?.Invoke();

            return _cachedResult;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Failed to check for plugin updates");
            _cachedResult ??= [];
            return _cachedResult;
        }
    }

    private PluginUpdateInfo? CheckPluginForUpdate(
        string pluginId, string pluginsDir,
        Dictionary<string, RegistryPlugin> registryLookup, DateTime now)
    {
        // Get the plugin's directory to check installation type
        var pluginDir = GetPluginDirectory(pluginId);
        if (pluginDir == null) return null;

        // Only check NuGet-installed plugins
        if (uninstallService.GetInstallationType(pluginDir) != PluginInstallationType.NuGet)
            return null;

        // Get the installed version from the manifest
        var manifest = pluginManager.GetPluginManifest(pluginId);
        if (manifest?.Version == null) return null;

        // Compare against registry
        if (!registryLookup.TryGetValue(pluginId, out var registryPlugin))
            return null;

        var installedVersion = manifest.Version.ToString(3);
        var hasUpdate = Version.TryParse(registryPlugin.Version, out var latestParsed)
            && latestParsed > manifest.Version;

        return new PluginUpdateInfo(
            pluginId,
            installedVersion,
            registryPlugin.Version,
            registryPlugin.Hash,
            hasUpdate,
            now);
    }

    public async Task UpdatePluginAsync(string packageId, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        await _updateLock.WaitAsync(cancellationToken);
        try
        {
            await UpdatePluginCoreAsync(packageId, progress, cancellationToken);
        }
        finally
        {
            _updateLock.Release();
        }
    }

    private async Task UpdatePluginCoreAsync(string packageId, IProgress<int>? progress, CancellationToken ct)
    {
        // Find the update info
        var updates = await CheckForUpdatesAsync();
        var updateInfo = updates.FirstOrDefault(u =>
            u.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase) && u.HasUpdate);

        if (updateInfo == null)
            throw new InvalidOperationException($"No update available for plugin '{packageId}'");

        var pluginsDir = Path.Combine(configService.TendrilHome, "plugins");
        var pluginDir = Path.Combine(pluginsDir, packageId);

        // Extract and resolve in a temp directory to avoid premature loading by PluginWatcher
        var tempDir = Path.Combine(Path.GetTempPath(), "ivy-plugin-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var id = packageId.ToLowerInvariant();
            var version = updateInfo.LatestVersion.ToLowerInvariant();
            var nupkgUrl = $"https://api.nuget.org/v3-flatcontainer/{id}/{version}/{id}.{version}.nupkg";

            // Download nupkg (0-10%)
            progress?.Report(0);
            using var http = httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(60);
            var nupkgBytes = await http.GetByteArrayAsync(nupkgUrl, ct);
            progress?.Report(10);

            // Verify SHA256 hash
            var computedHash = Convert.ToHexString(SHA256.HashData(nupkgBytes)).ToLowerInvariant();
            var expectedHash = updateInfo.LatestHash.ToLowerInvariant();
            if (computedHash != expectedHash)
            {
                throw new InvalidOperationException(
                    $"Plugin hash verification failed for '{packageId}'. " +
                    $"Expected: {expectedHash}, Got: {computedHash}");
            }

            // Extract nupkg to temp dir (10-20%)
            using var archive = new ZipArchive(new MemoryStream(nupkgBytes));
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;

                var destPath = Path.GetFullPath(Path.Combine(tempDir, entry.FullName));
                if (!destPath.StartsWith(tempDir + Path.DirectorySeparatorChar))
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                await using var entryStream = entry.Open();
                await using var fileStream = File.Create(destPath);
                await entryStream.CopyToAsync(fileStream, ct);
            }
            progress?.Report(20);

            // Resolve and download transitive dependencies (20-90%)
            var depProgress = new Progress<int>(p => progress?.Report(20 + (int)(p / 100.0 * 70)));
            await dependencyResolver.ResolveAndInstallDependenciesAsync(
                tempDir, packageId, updateInfo.LatestVersion, depProgress, ct);
            progress?.Report(90);

            // Unload the old plugin
            pluginManager.UnloadPlugin(packageId);

            // Remove old directory and move new one in
            if (Directory.Exists(pluginDir))
                Directory.Delete(pluginDir, recursive: true);
            Directory.Move(tempDir, pluginDir);

            // Eagerly load the updated plugin
            pluginManager.LoadPlugin(pluginDir);
            progress?.Report(100);

            logger.LogInformation("Updated plugin {PackageId} from {Old} to {New}",
                packageId, updateInfo.InstalledVersion, updateInfo.LatestVersion);

            // Invalidate cache so next check reflects the update
            _cachedResult = null;
            _lastCheckTime = DateTime.MinValue;
            UpdateStateChanged?.Invoke();
        }
        catch
        {
            // Clean up temp on failure
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
            throw;
        }
    }

    public async Task UpdateAllAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var updates = await CheckForUpdatesAsync();
        var pluginsToUpdate = updates.Where(u => u.HasUpdate).ToList();

        if (pluginsToUpdate.Count == 0) return;

        for (var i = 0; i < pluginsToUpdate.Count; i++)
        {
            var plugin = pluginsToUpdate[i];
            var baseProgress = (int)(i / (double)pluginsToUpdate.Count * 100);
            var nextProgress = (int)((i + 1) / (double)pluginsToUpdate.Count * 100);

            var perPluginProgress = new Progress<int>(p =>
                progress?.Report(baseProgress + (int)(p / 100.0 * (nextProgress - baseProgress))));

            try
            {
                await UpdatePluginAsync(plugin.PackageId, perPluginProgress, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to update plugin {PackageId}, continuing with remaining", plugin.PackageId);
            }
        }
    }

    private string? GetPluginDirectory(string pluginId)
    {
        if (pluginManager is not PluginLoader loader) return null;

        var plugin = loader.Plugins.FirstOrDefault(p =>
            p.Instance.Manifest.Id.Equals(pluginId, StringComparison.OrdinalIgnoreCase));
        return plugin?.Directory;
    }
}
