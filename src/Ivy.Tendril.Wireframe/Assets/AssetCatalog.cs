using System.Collections.Concurrent;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;

namespace Ivy.Tendril.Wireframe.Assets;

/// <summary>
/// Reads the prebuilt payload embedded in this assembly: the vendor JS bundle, the
/// stylesheets, the self-hosted fonts, the TypeScript definitions and the component
/// manifest. All of it is produced at tool-build time by build/vendor/build-all.mjs so
/// that nothing here needs node, npm or the network.
/// </summary>
public sealed class AssetCatalog
{
    private const string ResourceName = "wireframe-assets.zip";

    private readonly byte[] _zipBytes;
    private readonly ConcurrentDictionary<string, byte[]> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _entries;

    public static AssetCatalog Default { get; } = new();

    private AssetCatalog()
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' is missing. The build should have produced it from " +
                "artifacts/; run: cd build/vendor && npm install && node build-all.mjs");

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        _zipBytes = ms.ToArray();

        using var zip = OpenZip();
        _entries = new HashSet<string>(
            zip.Entries.Where(e => e.Length > 0 || !e.FullName.EndsWith('/')).Select(e => Normalize(e.FullName)),
            StringComparer.OrdinalIgnoreCase);

        // Identifies this exact payload. `setup` stamps it into .wireframe/.stamp so a
        // tool upgrade re-materializes the workspace instead of leaving stale types behind.
        Hash = Convert.ToHexString(SHA256.HashData(_zipBytes))[..16].ToLowerInvariant();
    }

    /// <summary>Short content hash of the whole payload.</summary>
    public string Hash { get; }

    public static string ToolVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    private ZipArchive OpenZip() => new(new MemoryStream(_zipBytes, writable: false), ZipArchiveMode.Read);

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');

    public bool Exists(string path) => _entries.Contains(Normalize(path));

    /// <summary>Entry paths under <paramref name="prefix"/>, relative to the payload root.</summary>
    public IEnumerable<string> List(string prefix)
    {
        prefix = Normalize(prefix);
        if (prefix.Length > 0 && !prefix.EndsWith('/')) prefix += "/";
        return _entries.Where(e => e.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).OrderBy(e => e);
    }

    /// <summary>
    /// Reads one asset. Results are cached: the whole payload is a couple of megabytes and
    /// the dev server re-reads the same handful of files on every page load.
    /// </summary>
    public byte[] Read(string path)
    {
        var key = Normalize(path);
        return _cache.GetOrAdd(key, static (k, self) =>
        {
            using var zip = self.OpenZip();
            var entry = zip.GetEntry(k)
                ?? throw new FileNotFoundException($"Asset '{k}' is not in the embedded payload.");
            using var s = entry.Open();
            using var ms = new MemoryStream(capacity: (int)entry.Length);
            s.CopyTo(ms);
            return ms.ToArray();
        }, this);
    }

    public string ReadText(string path) => System.Text.Encoding.UTF8.GetString(Read(path));

    public bool TryRead(string path, out byte[] bytes)
    {
        if (!Exists(path)) { bytes = []; return false; }
        bytes = Read(path);
        return true;
    }

    /// <summary>
    /// Writes every asset under <paramref name="prefix"/> into <paramref name="targetDir"/>,
    /// preserving relative structure. Used to materialize .wireframe/types/**.
    /// </summary>
    public int ExtractTo(string prefix, string targetDir)
    {
        var normalized = Normalize(prefix);
        if (normalized.Length > 0 && !normalized.EndsWith('/')) normalized += "/";

        var count = 0;
        foreach (var entry in List(normalized))
        {
            var relative = entry[normalized.Length..];
            var dest = Path.Combine(targetDir, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllBytes(dest, Read(entry));
            count++;
        }
        return count;
    }
}
