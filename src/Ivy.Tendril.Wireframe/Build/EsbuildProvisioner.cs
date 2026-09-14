using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Ivy.Tendril.Wireframe.Build;

/// <summary>
/// Locates the esbuild binary. esbuild ships as a standalone Go executable, which is
/// exactly why it is the bundler here: it needs no node runtime.
///
/// Resolution order, first hit wins:
///   1. WIREFRAME_ESBUILD   explicit override
///   2. global cache        a previous download, shared with the standalone wireframe tool
///   3. npm registry        about 11 MB, verified against the registry hash and cached
///
/// Tendril deliberately ships no esbuild of its own: it is a portable dotnet tool with no
/// runtime identifier to choose a binary by, and carrying all seven would add 74 MB to
/// every install. The first build on a machine pays for one download instead.
/// </summary>
public sealed class EsbuildProvisioner
{
    /// <summary>Kept in lockstep with build/vendor's pinned esbuild.</summary>
    public const string Version = "0.28.2";

    private static readonly string BinaryName =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "esbuild.exe" : "esbuild";

    public static string CacheRoot =>
        Environment.GetEnvironmentVariable("WIREFRAME_CACHE")
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "wireframe");

    /// <summary>The .NET RID we resolve npm platform packages against.</summary>
    public static string Rid
    {
        get
        {
            var arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.Arm64 => "arm64",
                Architecture.X86 => "ia32",
                _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            };
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return $"win-{arch}";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return $"osx-{arch}";
            return $"linux-{arch}";
        }
    }

    /// <summary>.NET RID -> npm platform package. esbuild's Linux builds are static Go
    /// binaries, so the same artifact serves glibc and musl.</summary>
    private static string NpmPackage => Rid switch
    {
        "win-x64" => "@esbuild/win32-x64",
        "win-arm64" => "@esbuild/win32-arm64",
        "win-ia32" => "@esbuild/win32-ia32",
        "linux-x64" => "@esbuild/linux-x64",
        "linux-arm64" => "@esbuild/linux-arm64",
        "osx-x64" => "@esbuild/darwin-x64",
        "osx-arm64" => "@esbuild/darwin-arm64",
        _ => throw new PlatformNotSupportedException(
            $"No esbuild binary is published for '{Rid}'. Set WIREFRAME_ESBUILD to a local esbuild."),
    };

    public async Task<string> ResolveAsync(CancellationToken ct = default)
    {
        foreach (var candidate in LocalCandidates())
        {
            if (File.Exists(candidate))
            {
                MakeExecutable(candidate);
                return candidate;
            }
        }

        return await DownloadAsync(ct);
    }

    /// <summary>Every place esbuild might already be, in preference order.</summary>
    private static IEnumerable<string> LocalCandidates()
    {
        var overridePath = Environment.GetEnvironmentVariable("WIREFRAME_ESBUILD");
        if (!string.IsNullOrWhiteSpace(overridePath)) yield return overridePath;

        // Previously downloaded.
        yield return CachedPath;
    }

    /// <summary>Where a downloaded binary lives. Reported by <c>tendril doctor</c>.</summary>
    public static string CachedPath => Path.Combine(CacheRoot, "esbuild", Version, Rid, BinaryName);

    private async Task<string> DownloadAsync(CancellationToken ct)
    {
        var pkg = NpmPackage;
        var target = Path.Combine(CacheRoot, "esbuild", Version, Rid, BinaryName);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        // The packument carries the tarball URL and its SHA-512 integrity.
        var doc = JsonDocument.Parse(
            await http.GetStringAsync($"https://registry.npmjs.org/{pkg}", ct));

        if (!doc.RootElement.GetProperty("versions").TryGetProperty(Version, out var v))
            throw new InvalidOperationException($"{pkg} has no version {Version} on the npm registry.");

        var dist = v.GetProperty("dist");
        var url = dist.GetProperty("tarball").GetString()!;
        var integrity = dist.TryGetProperty("integrity", out var i) ? i.GetString() : null;

        var bytes = await http.GetByteArrayAsync(url, ct);
        VerifyIntegrity(bytes, integrity, pkg);

        ExtractBinary(bytes, target);
        MakeExecutable(target);
        return target;
    }

    private static void VerifyIntegrity(byte[] bytes, string? integrity, string pkg)
    {
        if (string.IsNullOrEmpty(integrity)) return;

        var parts = integrity.Split('-', 2);
        if (parts.Length != 2) return;

        var actual = Convert.ToBase64String(parts[0] switch
        {
            "sha512" => SHA512.HashData(bytes),
            "sha256" => SHA256.HashData(bytes),
            "sha1" => SHA1.HashData(bytes),
            _ => [],
        });

        if (actual.Length > 0 && actual != parts[1])
            throw new InvalidOperationException(
                $"Integrity check failed for {pkg}: the downloaded tarball does not match the registry hash.");
    }

    /// <summary>Pulls the single binary out of the npm tarball (everything is under "package/").</summary>
    private static void ExtractBinary(byte[] tarGz, string target)
    {
        using var raw = new MemoryStream(tarGz);
        using var gz = new GZipStream(raw, CompressionMode.Decompress);
        using var tar = new TarReader(gz);

        while (tar.GetNextEntry() is { } entry)
        {
            var name = entry.Name.Replace('\\', '/');
            var isBinary = name is "package/esbuild.exe" or "package/bin/esbuild";
            if (!isBinary || entry.DataStream is null) continue;

            using var output = File.Create(target);
            entry.DataStream.CopyTo(output);
            return;
        }

        throw new InvalidOperationException("The esbuild tarball did not contain an esbuild binary.");
    }

    private static void MakeExecutable(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        try
        {
            var mode = File.GetUnixFileMode(path);
            File.SetUnixFileMode(path,
                mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Best effort: if the bit is already set, or the filesystem does not support
            // it, the subsequent exec will surface a clearer error than we could here.
        }
    }

    /// <summary>Used by `wireframe --version` style diagnostics.</summary>
    public static async Task<string?> TryGetVersionAsync(string binary, CancellationToken ct = default)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(binary, "--version")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            })!;
            var text = await p.StandardOutput.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);
            return text.Trim();
        }
        catch
        {
            return null;
        }
    }
}
