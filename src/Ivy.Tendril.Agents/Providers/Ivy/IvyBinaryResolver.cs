using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Ivy.Tendril.Agents.Helpers;

namespace Ivy.Tendril.Agents.Providers.Ivy;

public static class IvyBinaryResolver
{
    private static string? _cachedPath;

    public static string Resolve()
    {
        if (_cachedPath != null && File.Exists(_cachedPath)) return _cachedPath;

        var baseDir = AppContext.BaseDirectory;
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ivy-agent.exe" : "ivy-agent";

        // 1. Check bundled beside assembly or in bin subfolder
        var bundled = Path.Combine(baseDir, exeName);
        if (!File.Exists(bundled))
        {
            var bundledBin = Path.Combine(baseDir, "bin", exeName);
            if (File.Exists(bundledBin)) bundled = bundledBin;
        }

        // On macOS inside an app bundle, check Contents/MacOS, Contents/Resources, and Contents/Resources/bin
        if (!File.Exists(bundled) && RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && baseDir.Contains(".app/Contents/MacOS"))
        {
            var macOsDirect = Path.Combine(baseDir, exeName);
            if (File.Exists(macOsDirect))
            {
                bundled = macOsDirect;
            }
            else
            {
                var macOsBundled = Path.GetFullPath(Path.Combine(baseDir, "..", "Resources", exeName));
                if (File.Exists(macOsBundled))
                {
                    bundled = macOsBundled;
                }
                else
                {
                    var macOsBundledBin = Path.GetFullPath(Path.Combine(baseDir, "..", "Resources", "bin", exeName));
                    if (File.Exists(macOsBundledBin))
                    {
                        bundled = macOsBundledBin;
                    }
                }
            }
        }

        // Check local development workspace path if running from debug/build tree
        if (!File.Exists(bundled))
        {
            var localDevPath = GetLocalDevBinaryPath(exeName);
            if (localDevPath != null && File.Exists(localDevPath))
            {
                bundled = localDevPath;
            }
        }

        if (File.Exists(bundled))
        {
            EnsureExecutable(bundled);
            return _cachedPath = bundled;
        }

        // 2. Check Tendril managed binary directory (~/.tendril/bin)
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var tendrilManaged = Path.Combine(home, ".tendril", "bin", exeName);
        if (File.Exists(tendrilManaged))
        {
            EnsureExecutable(tendrilManaged);
            return _cachedPath = tendrilManaged;
        }

        // Fallback to default name if not found anywhere (do not check system PATH or external user directories)
        _cachedPath = null;
        return "ivy-agent";
    }

    public static async Task<string?> EnsureInstalledAsync(CancellationToken ct = default)
    {
        var resolved = Resolve();
        if (File.Exists(resolved)) return resolved;

        try
        {
            string os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" :
                        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "darwin" : "linux";
            string arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var installDir = Path.Combine(home, ".tendril", "bin");
            Directory.CreateDirectory(installDir);

            var tempDir = Path.Combine(Path.GetTempPath(), "ivy-agent-ensure-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempDir);

            try
            {
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("IvyTendril");

                string version = "v0.1.5";
                string extension = os == "windows" ? ".zip" : ".tar.gz";
                string archiveName = $"ivy-agent-cli-{os}-{arch}{extension}";
                string downloadUrl = $"https://cdn.ivy.app/ivy-agent-cli/releases/download/{version}/{archiveName}";
                string archivePath = Path.Combine(tempDir, archiveName);

                using (var stream = await httpClient.GetStreamAsync(downloadUrl, ct))
                using (var fileStream = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await stream.CopyToAsync(fileStream, ct);
                }

                if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    ZipFile.ExtractToDirectory(archivePath, tempDir, true);
                }
                else if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                {
                    var tarInfo = new ProcessStartInfo
                    {
                        FileName = "tar",
                        Arguments = $"-xzf \"{archivePath}\" -C \"{tempDir}\"",
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var tarProc = Process.Start(tarInfo);
                    if (tarProc != null)
                    {
                        await tarProc.WaitForExitAsync(ct);
                    }
                }

                string binaryName = os == "windows" ? "ivy-agent.exe" : "ivy-agent";
                var files = Directory.GetFiles(tempDir, binaryName, SearchOption.AllDirectories);
                var binarySource = files.FirstOrDefault();

                if (!string.IsNullOrEmpty(binarySource))
                {
                    string destPath = Path.Combine(installDir, binaryName);
                    if (File.Exists(destPath)) File.Delete(destPath);
                    File.Copy(binarySource, destPath, overwrite: true);
                    EnsureExecutable(destPath);
                    ResetCache();
                    return Resolve();
                }
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }
        }
        catch
        {
            // Silently fallback if offline
        }

        return File.Exists(resolved) ? resolved : null;
    }

    private static string? GetLocalDevBinaryPath(string exeName)
    {
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" :
                        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "darwin" : "linux";
            string arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";

            var candidate = Path.Combine(home, "git", "ivy", "ivy-agent-cli", "packages", "ivy-agent", "dist", $"ivy-agent-{os}-{arch}", "bin", exeName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        catch
        {
            // Ignore dev path errors
        }

        return null;
    }

    private static void EnsureExecutable(string path)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                var mode = File.GetUnixFileMode(path);
                if (!mode.HasFlag(UnixFileMode.UserExecute))
                {
                    File.SetUnixFileMode(path, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
                }
            }
            catch
            {
                // Best effort
            }
        }
    }

    public static void ResetCache() => _cachedPath = null;
}
