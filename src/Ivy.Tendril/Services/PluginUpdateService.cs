using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Xml.Linq;
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
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);

    private IReadOnlyList<PluginUpdateInfo>? _cachedResult;
    private DateTime _lastCheckTime = DateTime.MinValue;
    private string? _etag;
    private readonly SemaphoreSlim _updateLock = new(1, 1);

    public event Action? UpdateStateChanged;

    private record InstalledPlugin(string PackageId, string Version);
    private record CheckUpdatesRequest(InstalledPlugin[] InstalledPlugins);
    private record PluginUpdateEntry(
        [property: JsonPropertyName("packageId")] string PackageId,
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("hash")] string Hash,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("iconUrl")] string? IconUrl,
        [property: JsonPropertyName("iconKind")] string? IconKind,
        [property: JsonPropertyName("iconValue")] string? IconValue);

    public async Task<IReadOnlyList<PluginUpdateInfo>> CheckForUpdatesAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && _cachedResult != null && DateTime.UtcNow - _lastCheckTime < CacheDuration)
        {
            Console.WriteLine($"[CheckUpdates] Cache hit (age: {DateTime.UtcNow - _lastCheckTime:mm\\:ss})");
            return _cachedResult;
        }

        var now = DateTime.UtcNow;

        try
        {
            // Gather installed NuGet plugin versions
            var installedPlugins = GetInstalledNuGetPlugins();
            Console.WriteLine($"[CheckUpdates] Checking {installedPlugins.Length} installed plugins: {string.Join(", ", installedPlugins.Select(p => $"{p.PackageId}@{p.Version}"))}");

            using var http = httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(15);

            var request = new HttpRequestMessage(HttpMethod.Post, $"{tendrilArgs.ServicesUrl}/plugins/check-updates")
            {
                Content = JsonContent.Create(new CheckUpdatesRequest(installedPlugins))
            };

            if (_etag != null)
            {
                request.Headers.IfNoneMatch.ParseAdd(_etag);
                Console.WriteLine($"[CheckUpdates] Sending If-None-Match: {_etag}");
            }
            else
            {
                Console.WriteLine("[CheckUpdates] No ETag cached (first request)");
            }

            var response = await http.SendAsync(request);
            Console.WriteLine($"[CheckUpdates] Response: {(int)response.StatusCode} {response.StatusCode}");

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                // Registry hasn't changed — keep cached result
                _lastCheckTime = now;
                _cachedResult ??= [];
                Console.WriteLine($"[CheckUpdates] 304 — registry unchanged, returning {_cachedResult.Count} cached entries");
                return _cachedResult;
            }

            response.EnsureSuccessStatusCode();

            // Store ETag for next request
            if (response.Headers.ETag != null)
            {
                _etag = response.Headers.ETag.ToString();
                Console.WriteLine($"[CheckUpdates] Stored new ETag: {_etag}");
            }

            var updateEntries = await response.Content.ReadFromJsonAsync<PluginUpdateEntry[]>() ?? [];
            Console.WriteLine($"[CheckUpdates] Server returned {updateEntries.Length} update(s): {string.Join(", ", updateEntries.Select(e => $"{e.PackageId}→{e.Version}"))}");

            // Build update info list from the server's diff response
            var installedLookup = installedPlugins.ToDictionary(
                p => p.PackageId, p => p.Version, StringComparer.OrdinalIgnoreCase);

            var updates = updateEntries.Select(entry =>
            {
                installedLookup.TryGetValue(entry.PackageId, out var installedVersion);
                installedVersion ??= "0.0.0";

                // The server only returns entries where the version differs from installed,
                // so if it's in the response, it's an update.
                return new PluginUpdateInfo(
                    entry.PackageId, installedVersion, entry.Version,
                    entry.Hash, HasUpdate: true, now);
            }).ToList();

            var previousHadUpdates = _cachedResult?.Any(u => u.HasUpdate) == true;
            _cachedResult = updates;
            _lastCheckTime = now;

            if (updates.Any(u => u.HasUpdate) != previousHadUpdates)
                UpdateStateChanged?.Invoke();

            return _cachedResult;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.WriteLine($"[CheckUpdates] ERROR: {ex.GetType().Name}: {ex.Message}");
            logger.LogDebug(ex, "Failed to check for plugin updates");
            _cachedResult ??= [];
            return _cachedResult;
        }
    }

    private InstalledPlugin[] GetInstalledNuGetPlugins()
    {
        var installed = new List<InstalledPlugin>();

        foreach (var id in pluginManager.GetActivePluginIds())
        {
            var entry = GetInstalledPluginEntry(id);
            if (entry != null) installed.Add(entry);
        }

        foreach (var plugin in pluginManager.GetUnconfiguredPlugins())
        {
            var entry = GetInstalledPluginEntry(plugin.Id);
            if (entry != null) installed.Add(entry);
        }

        return installed.ToArray();
    }

    private InstalledPlugin? GetInstalledPluginEntry(string pluginId)
    {
        var pluginDir = GetPluginDirectory(pluginId);
        if (pluginDir == null) return null;

        if (uninstallService.GetInstallationType(pluginDir) != PluginInstallationType.NuGet)
            return null;

        var version = GetInstalledNuGetVersion(pluginDir);
        if (version == null) return null;

        return new InstalledPlugin(pluginId, version);
    }

    /// <summary>
    /// Reads the package version from the .nuspec file in the installed plugin directory.
    /// </summary>
    private static string? GetInstalledNuGetVersion(string pluginDir)
    {
        try
        {
            var nuspecPath = Directory.GetFiles(pluginDir, "*.nuspec", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
            if (nuspecPath == null) return null;

            var doc = XDocument.Load(nuspecPath);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
            return doc.Root?.Element(ns + "metadata")?.Element(ns + "version")?.Value;
        }
        catch
        {
            return null;
        }
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
        Console.WriteLine($"[UpdatePlugin] Starting update for '{packageId}'");

        // Find the update info
        var updates = await CheckForUpdatesAsync();
        var updateInfo = updates.FirstOrDefault(u =>
            u.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase) && u.HasUpdate);

        if (updateInfo == null)
        {
            Console.WriteLine($"[UpdatePlugin] No update found for '{packageId}' (updates count: {updates.Count}, hasUpdate entries: {updates.Count(u => u.HasUpdate)})");
            throw new InvalidOperationException($"No update available for plugin '{packageId}'");
        }

        Console.WriteLine($"[UpdatePlugin] Updating '{packageId}' from {updateInfo.InstalledVersion} to {updateInfo.LatestVersion}");

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
            Console.WriteLine($"[UpdatePlugin] Downloading {nupkgUrl}");
            progress?.Report(0);
            using var http = httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(60);
            var nupkgBytes = await http.GetByteArrayAsync(nupkgUrl, ct);
            Console.WriteLine($"[UpdatePlugin] Downloaded {nupkgBytes.Length} bytes");
            progress?.Report(10);

            // Verify SHA256 hash
            var computedHash = Convert.ToBase64String(SHA256.HashData(nupkgBytes));
            var expectedHash = updateInfo.LatestHash;
            Console.WriteLine($"[UpdatePlugin] Hash check — expected: {expectedHash}, computed: {computedHash}");
            if (!string.Equals(computedHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[UpdatePlugin] HASH MISMATCH — aborting");
                throw new InvalidOperationException(
                    $"Plugin hash verification failed for '{packageId}'. " +
                    $"Expected: {expectedHash}, Got: {computedHash}");
            }

            // Extract nupkg to temp dir (10-20%)
            Console.WriteLine($"[UpdatePlugin] Extracting to {tempDir}");
            using var archive = new ZipArchive(new MemoryStream(nupkgBytes));
            var extractedCount = 0;
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
                extractedCount++;
            }
            Console.WriteLine($"[UpdatePlugin] Extracted {extractedCount} files");
            progress?.Report(20);

            // Resolve and download transitive dependencies (20-90%)
            Console.WriteLine($"[UpdatePlugin] Resolving dependencies for {packageId} {updateInfo.LatestVersion}");
            var depProgress = new Progress<int>(p => progress?.Report(20 + (int)(p / 100.0 * 70)));
            await dependencyResolver.ResolveAndInstallDependenciesAsync(
                tempDir, packageId, updateInfo.LatestVersion, depProgress, ct);
            Console.WriteLine($"[UpdatePlugin] Dependencies resolved");
            progress?.Report(90);

            // Unload the old plugin
            Console.WriteLine($"[UpdatePlugin] Unloading old plugin");
            pluginManager.UnloadPlugin(packageId);

            // Remove old directory and move new one in
            if (Directory.Exists(pluginDir))
                Directory.Delete(pluginDir, recursive: true);
            Directory.Move(tempDir, pluginDir);
            Console.WriteLine($"[UpdatePlugin] Moved to {pluginDir}");

            // Eagerly load the updated plugin
            Console.WriteLine($"[UpdatePlugin] Loading updated plugin");
            pluginManager.LoadPlugin(pluginDir);
            progress?.Report(100);

            Console.WriteLine($"[UpdatePlugin] Successfully updated '{packageId}' to {updateInfo.LatestVersion}");
            logger.LogInformation("Updated plugin {PackageId} from {Old} to {New}",
                packageId, updateInfo.InstalledVersion, updateInfo.LatestVersion);

            // Invalidate cache so next check reflects the update
            _cachedResult = null;
            _lastCheckTime = DateTime.MinValue;
            _etag = null;
            UpdateStateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UpdatePlugin] FAILED for '{packageId}': {ex.GetType().Name}: {ex.Message}");
            // Clean up temp on failure
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
            throw;
        }
    }

    public async Task UpdateAllAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        Console.WriteLine("[UpdateAll] Starting update all");
        var updates = await CheckForUpdatesAsync();
        var pluginsToUpdate = updates.Where(u => u.HasUpdate).ToList();
        Console.WriteLine($"[UpdateAll] {pluginsToUpdate.Count} plugin(s) to update: {string.Join(", ", pluginsToUpdate.Select(p => $"{p.PackageId} {p.InstalledVersion}→{p.LatestVersion}"))}");

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
                Console.WriteLine($"[UpdateAll] Plugin '{plugin.PackageId}' failed: {ex.GetType().Name}: {ex.Message}");
                logger.LogWarning(ex, "Failed to update plugin {PackageId}, continuing with remaining", plugin.PackageId);
            }
        }
        Console.WriteLine("[UpdateAll] Finished");
    }

    private string? GetPluginDirectory(string pluginId)
    {
        if (pluginManager is not PluginLoader loader) return null;

        var plugin = loader.Plugins.FirstOrDefault(p =>
            p.Instance.Manifest.Id.Equals(pluginId, StringComparison.OrdinalIgnoreCase));
        return plugin?.Directory;
    }
}
