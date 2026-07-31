using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services;

/// <summary>
/// Resolves and downloads transitive NuGet dependencies for plugins at install time.
/// Uses the NuGet V3 flat-container API directly — no NuGet client libraries required.
/// </summary>
internal class NuGetDependencyResolver(IHttpClientFactory httpClientFactory, ILogger<NuGetDependencyResolver> logger)
{
    private const string NuGetFlatContainerBase = "https://api.nuget.org/v3-flatcontainer";
    private const int MaxRetries = 3;

    private static readonly string[] TfmPriority =
        ["net10.0", "net9.0", "net8.0", "net7.0", "net6.0", "netstandard2.1", "netstandard2.0"];

    private static readonly HashSet<string> SharedPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ivy",
        "Ivy.Plugin.Abstractions",
        "Ivy.Tendril.Plugin.Abstractions",
        "Ivy.Tendril.Plugin.Extended.Abstractions",
        "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Extensions.DependencyInjection.Abstractions",
        "Microsoft.Extensions.Logging.Abstractions",
    };

    /// <summary>
    /// Resolves the full transitive dependency graph for a plugin and downloads/extracts
    /// dependency DLLs into the plugin's lib directory.
    /// </summary>
    /// <param name="pluginDir">The extracted plugin directory (contains the .nuspec).</param>
    /// <param name="packageId">The plugin's NuGet package ID.</param>
    /// <param name="version">The plugin's version.</param>
    /// <param name="progress">Reports progress from 0 to 100.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task ResolveAndInstallDependenciesAsync(string pluginDir, string packageId, string version,
        IProgress<int>? progress = null, CancellationToken ct = default)
    {
        // The nuspec inside the nupkg preserves original casing; find it case-insensitively
        var nuspecPath = Directory.GetFiles(pluginDir, "*.nuspec", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (nuspecPath == null)
        {
            logger.LogDebug("No .nuspec found in {Dir}, skipping dependency resolution", pluginDir);
            progress?.Report(100);
            return;
        }

        progress?.Report(0);
        var hostAssemblies = BuildHostAssemblySet();
        var resolved = await ResolveTransitiveDependenciesAsync(packageId, version, progress, ct);

        if (resolved.Count == 0)
        {
            logger.LogDebug("No dependencies to resolve for {PackageId}", packageId);
            progress?.Report(100);
            return;
        }

        logger.LogInformation("Resolved {Count} dependencies for {PackageId}, downloading...", resolved.Count, packageId);

        // Determine the target directory for dependency DLLs
        var targetLibDir = FindOrCreateLibDir(pluginDir);

        for (var i = 0; i < resolved.Count; i++)
        {
            var dep = resolved[i];
            try
            {
                await DownloadAndExtractDependencyAsync(dep, targetLibDir, hostAssemblies, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to download dependency {PackageId} {Version}, skipping", dep.Id, dep.Version);
            }

            // Report progress: 30-100% range for downloads
            progress?.Report(30 + (int)((i + 1) / (double)resolved.Count * 70));
        }
    }

    private async Task<IReadOnlyList<ResolvedPackage>> ResolveTransitiveDependenciesAsync(
        string rootPackageId, string rootVersion, IProgress<int>? progress, CancellationToken ct)
    {
        // BFS through the dependency graph
        var visited = new Dictionary<string, NuGetVersion>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string Id, string Version)>();
        var resolvedCount = 0;

        // Seed with the root package's direct dependencies
        var rootDeps = await FetchDependenciesAsync(rootPackageId, rootVersion, ct);
        foreach (var dep in rootDeps)
        {
            if (SharedPackages.Contains(dep.Id)) continue;
            queue.Enqueue((dep.Id, dep.Version));
        }

        while (queue.Count > 0)
        {
            var (id, versionStr) = queue.Dequeue();
            ct.ThrowIfCancellationRequested();

            if (SharedPackages.Contains(id)) continue;

            var version = NuGetVersion.Parse(versionStr);

            // If we've already visited this package at an equal or higher version, skip
            if (visited.TryGetValue(id, out var existingVersion) && existingVersion >= version)
                continue;

            visited[id] = version;
            resolvedCount++;

            // Report progress: 0-30% range for graph resolution
            // Use an asymptotic curve so it never reaches 30% until done
            progress?.Report(Math.Min(29, (int)(30.0 * resolvedCount / (resolvedCount + queue.Count + 1))));

            // Fetch this package's own dependencies and enqueue them
            try
            {
                var deps = await FetchDependenciesAsync(id, versionStr, ct);
                foreach (var dep in deps)
                {
                    if (SharedPackages.Contains(dep.Id)) continue;
                    queue.Enqueue((dep.Id, dep.Version));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to fetch dependencies for {PackageId} {Version}", id, versionStr);
            }
        }

        progress?.Report(30);
        return visited.Select(kvp => new ResolvedPackage(kvp.Key, kvp.Value.ToString())).ToList();
    }

    private async Task<IReadOnlyList<NuspecDependency>> FetchDependenciesAsync(
        string packageId, string version, CancellationToken ct)
    {
        // NuGet flat-container nuspec URL: {base}/{id}/{version}/{id}.nuspec (no version in filename)
        var nuspecUrl = $"{NuGetFlatContainerBase}/{packageId.ToLowerInvariant()}/{version.ToLowerInvariant()}/{packageId.ToLowerInvariant()}.nuspec";
        var nuspecXml = await FetchStringWithRetryAsync(nuspecUrl, ct);
        if (nuspecXml == null) return [];

        return ParseNuspecDependencies(nuspecXml);
    }

    private static IReadOnlyList<NuspecDependency> ParseNuspecDependencies(string nuspecXml)
    {
        var doc = XDocument.Parse(nuspecXml);
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

        var dependencies = doc.Root?.Element(ns + "metadata")?.Element(ns + "dependencies");
        if (dependencies == null) return [];

        // Try to find the best matching dependency group for our target framework
        var groups = dependencies.Elements(ns + "group").ToList();

        if (groups.Count == 0)
        {
            // No groups — dependencies are listed directly (legacy format)
            return dependencies.Elements(ns + "dependency")
                .Select(ParseDependencyElement)
                .Where(d => d != null)
                .Cast<NuspecDependency>()
                .ToList();
        }

        // Find the best TFM group
        var selectedGroup = SelectBestTfmGroup(groups, ns);
        if (selectedGroup == null) return [];

        return selectedGroup.Elements(ns + "dependency")
            .Select(ParseDependencyElement)
            .Where(d => d != null)
            .Cast<NuspecDependency>()
            .ToList();
    }

    private static XElement? SelectBestTfmGroup(List<XElement> groups, XNamespace ns)
    {
        // First, try exact/compatible TFM matches in priority order
        foreach (var tfm in TfmPriority)
        {
            var match = groups.FirstOrDefault(g =>
            {
                var targetFw = g.Attribute("targetFramework")?.Value;
                return NormalizeTfm(targetFw).Equals(tfm, StringComparison.OrdinalIgnoreCase);
            });
            if (match != null) return match;
        }

        // Fall back to a group with no targetFramework specified (applies to all)
        return groups.FirstOrDefault(g => string.IsNullOrEmpty(g.Attribute("targetFramework")?.Value));
    }

    private static string NormalizeTfm(string? tfm)
    {
        if (string.IsNullOrEmpty(tfm)) return "";

        // Handle long-form TFMs like ".NETStandard,Version=v2.0" or ".NETCoreApp,Version=v10.0"
        tfm = tfm.Trim().TrimStart('.');
        if (tfm.StartsWith("NETStandard,Version=v", StringComparison.OrdinalIgnoreCase))
        {
            var ver = tfm["NETStandard,Version=v".Length..];
            return $"netstandard{ver}";
        }
        if (tfm.StartsWith("NETCoreApp,Version=v", StringComparison.OrdinalIgnoreCase))
        {
            var ver = tfm["NETCoreApp,Version=v".Length..];
            // net5.0+ uses "netX.Y" format
            if (Version.TryParse(ver, out var parsed) && parsed.Major >= 5)
                return $"net{ver}";
            return $"netcoreapp{ver}";
        }

        // Already short-form (net10.0, netstandard2.1, etc.)
        return tfm.ToLowerInvariant();
    }

    private static NuspecDependency? ParseDependencyElement(XElement element)
    {
        var id = element.Attribute("id")?.Value;
        var versionRange = element.Attribute("version")?.Value;

        if (string.IsNullOrEmpty(id)) return null;

        var version = ParseVersionRange(versionRange);
        if (version == null) return null;

        return new NuspecDependency(id, version);
    }

    /// <summary>
    /// Parses a NuGet version range and returns the minimum (lower bound) version.
    /// Examples: "1.0.0" → "1.0.0", "[1.0.0, )" → "1.0.0", "(1.0.0, 2.0.0]" → "1.0.0"
    /// </summary>
    internal static string? ParseVersionRange(string? versionRange)
    {
        if (string.IsNullOrWhiteSpace(versionRange)) return null;

        versionRange = versionRange.Trim();

        // Simple version (no brackets): treat as minimum
        if (!versionRange.StartsWith('[') && !versionRange.StartsWith('('))
            return versionRange;

        // Range format: [min, max] or (min, max) or [exact] etc.
        var inner = versionRange.TrimStart('[', '(').TrimEnd(']', ')');

        // Exact version: [1.0.0]
        if (!inner.Contains(','))
            return inner.Trim();

        // Range: split on comma, take the lower bound
        var parts = inner.Split(',', 2);
        var lower = parts[0].Trim();

        // "(, 2.0)" means no lower bound — use empty to signal "any"
        if (string.IsNullOrEmpty(lower)) return null;

        return lower;
    }

    private async Task DownloadAndExtractDependencyAsync(
        ResolvedPackage package, string targetLibDir, HashSet<string> hostAssemblies, CancellationToken ct)
    {
        var id = package.Id.ToLowerInvariant();
        var version = package.Version.ToLowerInvariant();
        var nupkgUrl = $"{NuGetFlatContainerBase}/{id}/{version}/{id}.{version}.nupkg";

        var nupkgBytes = await FetchBytesWithRetryAsync(nupkgUrl, ct);
        if (nupkgBytes == null)
        {
            logger.LogWarning("Could not download {PackageId} {Version}", package.Id, package.Version);
            return;
        }

        using var archive = new ZipArchive(new MemoryStream(nupkgBytes), ZipArchiveMode.Read);

        // Find all lib/ entries and group by TFM folder
        var libEntries = archive.Entries
            .Where(e => e.FullName.StartsWith("lib/", StringComparison.OrdinalIgnoreCase)
                        && e.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .GroupBy(e =>
            {
                // Extract TFM from path: "lib/net10.0/Foo.dll" → "net10.0"
                var parts = e.FullName.Split('/');
                return parts.Length >= 3 ? parts[1].ToLowerInvariant() : "";
            })
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .ToDictionary(g => g.Key, g => g.ToList());

        if (libEntries.Count == 0)
        {
            logger.LogDebug("No lib/ DLLs in {PackageId} {Version}, skipping", package.Id, package.Version);
            return;
        }

        // Select the best TFM folder
        string? bestTfm = null;
        foreach (var tfm in TfmPriority)
        {
            if (libEntries.ContainsKey(tfm))
            {
                bestTfm = tfm;
                break;
            }
        }

        if (bestTfm == null)
        {
            logger.LogDebug("No compatible TFM in {PackageId} {Version} (available: {Tfms})",
                package.Id, package.Version, string.Join(", ", libEntries.Keys));
            return;
        }

        // Extract DLLs from the best TFM folder into the target lib directory
        var extractedCount = 0;
        foreach (var entry in libEntries[bestTfm])
        {
            var assemblyName = Path.GetFileNameWithoutExtension(entry.Name);

            // Skip if this assembly is already provided by the host
            if (hostAssemblies.Contains(assemblyName))
            {
                logger.LogDebug("Skipping {Assembly} from {PackageId} — already provided by host", assemblyName, package.Id);
                continue;
            }

            // Skip if already exists (e.g., from a different dependency or the plugin itself)
            var destPath = Path.Combine(targetLibDir, entry.Name);
            if (File.Exists(destPath)) continue;

            await using var entryStream = entry.Open();
            await using var fileStream = File.Create(destPath);
            await entryStream.CopyToAsync(fileStream, ct);
            extractedCount++;
        }

        if (extractedCount > 0)
            logger.LogDebug("Extracted {Count} DLLs from {PackageId} {Version}", extractedCount, package.Id, package.Version);
    }

    /// <summary>
    /// Finds the best lib/net* directory in the extracted plugin, or creates lib/net10.0/.
    /// </summary>
    private static string FindOrCreateLibDir(string pluginDir)
    {
        var libDir = Path.Combine(pluginDir, "lib");
        if (!Directory.Exists(libDir))
        {
            var targetDir = Path.Combine(libDir, "net10.0");
            Directory.CreateDirectory(targetDir);
            return targetDir;
        }

        // Find the best existing TFM subdirectory
        var subdirs = Directory.GetDirectories(libDir)
            .Select(d => Path.GetFileName(d).ToLowerInvariant())
            .ToHashSet();

        foreach (var tfm in TfmPriority)
        {
            if (subdirs.Contains(tfm))
                return Path.Combine(libDir, tfm);
        }

        // Fallback: create net10.0
        var fallbackDir = Path.Combine(libDir, "net10.0");
        Directory.CreateDirectory(fallbackDir);
        return fallbackDir;
    }

    /// <summary>
    /// Builds a set of assembly names already available on the host (runtime + app directory).
    /// </summary>
    private static HashSet<string> BuildHostAssemblySet()
    {
        var assemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // .NET shared framework directory (System.*, Microsoft.*, etc.)
        var runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
        if (Directory.Exists(runtimeDir))
        {
            foreach (var dll in Directory.GetFiles(runtimeDir, "*.dll"))
            {
                assemblies.Add(Path.GetFileNameWithoutExtension(dll));
            }
        }

        // Tendril's own output directory (Microsoft.Extensions.*, Ivy.*, etc.)
        var appDir = System.AppContext.BaseDirectory;
        if (Directory.Exists(appDir) && !string.Equals(appDir.TrimEnd(Path.DirectorySeparatorChar),
                runtimeDir.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            foreach (var dll in Directory.GetFiles(appDir, "*.dll"))
            {
                assemblies.Add(Path.GetFileNameWithoutExtension(dll));
            }
        }

        return assemblies;
    }

    private async Task<string?> FetchStringWithRetryAsync(string url, CancellationToken ct)
    {
        using var http = httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);

        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var response = await http.GetAsync(url, ct);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return null;
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < MaxRetries)
            {
                logger.LogDebug(ex, "Attempt {Attempt}/{Max} failed for {Url}", attempt, MaxRetries, url);
                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), ct);
            }
        }

        return null;
    }

    private async Task<byte[]?> FetchBytesWithRetryAsync(string url, CancellationToken ct)
    {
        using var http = httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(60);

        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var response = await http.GetAsync(url, ct);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return null;
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < MaxRetries)
            {
                logger.LogDebug(ex, "Attempt {Attempt}/{Max} failed for {Url}", attempt, MaxRetries, url);
                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), ct);
            }
        }

        return null;
    }

    private record ResolvedPackage(string Id, string Version);

    private record NuspecDependency(string Id, string Version);

    /// <summary>
    /// Simple NuGet version comparison that handles semver (major.minor.patch[-prerelease]).
    /// </summary>
    internal readonly struct NuGetVersion : IComparable<NuGetVersion>
    {
        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }
        public int Revision { get; }
        public string? Prerelease { get; }
        private readonly string _original;

        private NuGetVersion(int major, int minor, int patch, int revision, string? prerelease, string original)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            Revision = revision;
            Prerelease = prerelease;
            _original = original;
        }

        public static NuGetVersion Parse(string version)
        {
            var prerelease = (string?)null;
            var dashIdx = version.IndexOf('-');
            var versionPart = dashIdx >= 0 ? version[..dashIdx] : version;
            if (dashIdx >= 0) prerelease = version[(dashIdx + 1)..];

            var parts = versionPart.Split('.');
            var major = parts.Length > 0 && int.TryParse(parts[0], out var m) ? m : 0;
            var minor = parts.Length > 1 && int.TryParse(parts[1], out var mi) ? mi : 0;
            var patch = parts.Length > 2 && int.TryParse(parts[2], out var p) ? p : 0;
            var revision = parts.Length > 3 && int.TryParse(parts[3], out var r) ? r : 0;

            return new NuGetVersion(major, minor, patch, revision, prerelease, version);
        }

        public int CompareTo(NuGetVersion other)
        {
            var c = Major.CompareTo(other.Major);
            if (c != 0) return c;
            c = Minor.CompareTo(other.Minor);
            if (c != 0) return c;
            c = Patch.CompareTo(other.Patch);
            if (c != 0) return c;
            c = Revision.CompareTo(other.Revision);
            if (c != 0) return c;

            // Pre-release versions sort lower than release
            if (Prerelease == null && other.Prerelease != null) return 1;
            if (Prerelease != null && other.Prerelease == null) return -1;
            return string.Compare(Prerelease, other.Prerelease, StringComparison.OrdinalIgnoreCase);
        }

        public static bool operator >=(NuGetVersion left, NuGetVersion right) => left.CompareTo(right) >= 0;
        public static bool operator <=(NuGetVersion left, NuGetVersion right) => left.CompareTo(right) <= 0;
        public static bool operator >(NuGetVersion left, NuGetVersion right) => left.CompareTo(right) > 0;
        public static bool operator <(NuGetVersion left, NuGetVersion right) => left.CompareTo(right) < 0;

        public override string ToString() => _original;
    }
}
