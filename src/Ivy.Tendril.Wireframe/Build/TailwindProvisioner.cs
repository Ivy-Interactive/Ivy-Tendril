using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Ivy.Tendril.Wireframe.Build;

/// <summary>
/// Provisions the official Tailwind standalone CLI for <c>--tailwind jit</c>.
///
/// This is opt-in rather than the default because the binary is ~107 MB, far too much
/// to make every user download for a tool whose selling point is a sub-second `setup`.
/// The embedded superset covers the common utility surface; this exists for the cases it
/// cannot, chiefly arbitrary values (<c>w-[347px]</c>) and exotic variants.
/// </summary>
public sealed class TailwindProvisioner
{
    /// <summary>Matches the version build/vendor compiled the embedded superset with.</summary>
    public const string Version = "4.3.3";

    private const string ReleaseBase =
        "https://github.com/tailwindlabs/tailwindcss/releases/download";

    public static string CacheRoot => EsbuildProvisioner.CacheRoot;

    /// <summary>The release asset name for this platform.</summary>
    public static string AssetName
    {
        get
        {
            var arm = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return arm
                    ? throw new PlatformNotSupportedException(
                        "Tailwind does not publish a standalone CLI for Windows on ARM64. " +
                        "Use the default (embedded) Tailwind mode.")
                    : "tailwindcss-windows-x64.exe";

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return arm ? "tailwindcss-macos-arm64" : "tailwindcss-macos-x64";

            // The musl builds are published separately; prefer them when we can tell.
            var musl = File.Exists("/lib/ld-musl-x86_64.so.1") || File.Exists("/lib/ld-musl-aarch64.so.1");
            return (arm, musl) switch
            {
                (true, true) => "tailwindcss-linux-arm64-musl",
                (true, false) => "tailwindcss-linux-arm64",
                (false, true) => "tailwindcss-linux-x64-musl",
                (false, false) => "tailwindcss-linux-x64",
            };
        }
    }

    private static string BinaryPath =>
        Path.Combine(CacheRoot, "tailwind", Version, AssetName);

    /// <summary>
    /// Returns the cached binary, downloading it once if needed.
    /// <paramref name="onProgress"/> reports megabytes so the caller can show something:
    /// a silent 107 MB download looks like a hang.
    /// </summary>
    public async Task<string> ResolveAsync(
        Action<long, long?>? onProgress = null, CancellationToken ct = default)
    {
        var overridePath = Environment.GetEnvironmentVariable("WIREFRAME_TAILWIND");
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
            return overridePath;

        var target = BinaryPath;
        if (File.Exists(target)) return target;

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };

        var expected = await FetchChecksumAsync(http, AssetName, ct);

        // Download beside the target, then move: an interrupted download must never leave
        // a truncated binary that looks cached on the next run.
        var partial = target + ".partial";
        try
        {
            using (var response = await http.GetAsync(
                $"{ReleaseBase}/v{Version}/{AssetName}", HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength;

                await using var source = await response.Content.ReadAsStreamAsync(ct);
                await using var destination = File.Create(partial);

                var buffer = new byte[81920];
                long written = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                    written += read;
                    onProgress?.Invoke(written, total);
                }
            }

            if (expected is not null)
            {
                await using var stream = File.OpenRead(partial);
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
                if (actual != expected)
                    throw new InvalidOperationException(
                        $"Checksum mismatch for {AssetName}: the download does not match the published release hash.");
            }

            File.Move(partial, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(partial)) File.Delete(partial);
        }

        MakeExecutable(target);
        return target;
    }

    /// <summary>Reads the release's sha256sums.txt. Returns null if it is unavailable, so a
    /// missing checksum file degrades to an unverified download rather than a hard failure.</summary>
    private static async Task<string?> FetchChecksumAsync(HttpClient http, string asset, CancellationToken ct)
    {
        try
        {
            var text = await http.GetStringAsync($"{ReleaseBase}/v{Version}/sha256sums.txt", ct);
            foreach (var line in text.Split('\n'))
            {
                var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[^1].TrimStart('*') == asset)
                    return parts[0].ToLowerInvariant();
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            // Fall through: the download itself will still fail loudly if it is broken.
        }
        return null;
    }

    private static void MakeExecutable(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        try
        {
            File.SetUnixFileMode(path, File.GetUnixFileMode(path)
                | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The subsequent exec will report this more clearly than we can.
        }
    }
}
