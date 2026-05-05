using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace Ivy.Tendril.Apps.Onboarding;

internal static class SoftwareInstaller
{
    public static bool CanAutoInstall(string key) => key switch
    {
        "powershell" => true,
        "gh" => true,
        "pandoc" => true,
        _ => false
    };

    public static Task<(bool Success, string Message)> InstallAsync(string key) => key switch
    {
        "powershell" => Run("dotnet", "tool install --global PowerShell"),
        "gh" => InstallToolAsync("gh", "cli/cli"),
        "pandoc" => InstallToolAsync("pandoc", "jgm/pandoc"),
        _ => Task.FromResult<(bool, string)>((false, $"No installer configured for '{key}'."))
    };

    private static Task<(bool, string)> InstallToolAsync(string tool, string ownerRepo)
    {
        if (OperatingSystem.IsWindows())
        {
            var wingetId = tool switch
            {
                "gh" => "GitHub.cli",
                "pandoc" => "JohnMacFarlane.Pandoc",
                _ => null
            };
            if (wingetId is null)
            {
                return Task.FromResult<(bool, string)>((false, $"No Windows installer for '{tool}'."));
            }
            return Run("winget",
                $"install --id {wingetId} -e --silent --accept-source-agreements --accept-package-agreements");
        }

        return InstallFromGitHubReleaseAsync(tool, ownerRepo);
    }

    private static async Task<(bool, string)> InstallFromGitHubReleaseAsync(string tool, string ownerRepo)
    {
        try
        {
            var osTokens = OperatingSystem.IsMacOS()
                ? new[] { "macOS" }
                : new[] { "linux" };
            var archTokens = RuntimeInformation.OSArchitecture switch
            {
                Architecture.Arm64 => new[] { "arm64" },
                Architecture.X64 => new[] { "amd64", "x86_64" },
                _ => new[] { RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant() }
            };

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Ivy.Tendril", "1.0"));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            var releaseJson = await http.GetStringAsync(
                $"https://api.github.com/repos/{ownerRepo}/releases/latest");
            var release = JsonNode.Parse(releaseJson)
                ?? throw new InvalidOperationException("Empty release response.");
            var assets = release["assets"]?.AsArray()
                ?? throw new InvalidOperationException("No 'assets' array in release JSON.");

            string? assetName = null, assetUrl = null;
            foreach (var asset in assets)
            {
                var name = asset?["name"]?.GetValue<string>();
                var url = asset?["browser_download_url"]?.GetValue<string>();
                if (name is null || url is null) continue;
                if (!IsArchive(name)) continue;
                if (!osTokens.Any(t => name.Contains(t, StringComparison.OrdinalIgnoreCase))) continue;
                if (!archTokens.Any(t => name.Contains(t, StringComparison.OrdinalIgnoreCase))) continue;
                assetName = name;
                assetUrl = url;
                break;
            }

            if (assetName is null || assetUrl is null)
            {
                return (false,
                    $"Couldn't find a {tool} release asset for {string.Join("/", osTokens)} {string.Join("/", archTokens)}. " +
                    $"See https://github.com/{ownerRepo}/releases.");
            }

            var binDir = GetTendrilBinDir();
            Directory.CreateDirectory(binDir);

            var workDir = Path.Combine(Path.GetTempPath(), $"tendril-install-{tool}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(workDir);
            try
            {
                var archivePath = Path.Combine(workDir, assetName);
                await using (var stream = await http.GetStreamAsync(assetUrl))
                await using (var file = File.Create(archivePath))
                {
                    await stream.CopyToAsync(file);
                }

                var extractDir = Path.Combine(workDir, "extracted");
                Directory.CreateDirectory(extractDir);

                if (assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    ZipFile.ExtractToDirectory(archivePath, extractDir);
                }
                else
                {
                    var (tarOk, tarMsg) = await Run("tar", $"-xzf \"{archivePath}\" -C \"{extractDir}\"");
                    if (!tarOk) return (false, $"tar extraction failed: {tarMsg}");
                }

                var binarySrc = Directory
                    .EnumerateFiles(extractDir, tool, SearchOption.AllDirectories)
                    .FirstOrDefault(p => string.Equals(Path.GetFileName(p), tool, StringComparison.Ordinal));
                if (binarySrc is null)
                {
                    return (false, $"Couldn't find '{tool}' binary inside {assetName}.");
                }

                var binaryDst = Path.Combine(binDir, tool);
                File.Copy(binarySrc, binaryDst, overwrite: true);

                var (chmodOk, chmodMsg) = await Run("chmod", $"+x \"{binaryDst}\"");
                if (!chmodOk) return (false, $"chmod failed: {chmodMsg}");

                EnsureOnPath(binDir);

                return (true, $"Installed {tool} to {binaryDst}.");
            }
            finally
            {
                try { Directory.Delete(workDir, recursive: true); } catch { /* best-effort */ }
            }
        }
        catch (Exception ex)
        {
            return (false, $"Install failed: {ex.Message}");
        }
    }

    private static bool IsArchive(string name) =>
        name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase);

    private static string GetTendrilBinDir()
    {
        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        if (string.IsNullOrWhiteSpace(tendrilHome))
        {
            tendrilHome = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".tendril");
        }
        return Path.Combine(tendrilHome, "bin");
    }

    private static void EnsureOnPath(string dir)
    {
        var sep = Path.PathSeparator;
        var current = Environment.GetEnvironmentVariable("PATH") ?? "";
        var normalized = Path.TrimEndingDirectorySeparator(dir);
        var entries = current.Split(sep, StringSplitOptions.RemoveEmptyEntries);
        if (entries.Any(e => string.Equals(Path.TrimEndingDirectorySeparator(e), normalized, StringComparison.Ordinal)))
        {
            return;
        }
        Environment.SetEnvironmentVariable("PATH", $"{dir}{sep}{current}");
    }

    private static async Task<(bool, string)> Run(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "cmd.exe" : fileName,
                Arguments = OperatingSystem.IsWindows()
                    ? $"/S /c \"{fileName} {arguments}\""
                    : arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc is null) return (false, $"Failed to start '{fileName}'.");

            // Read both streams concurrently — reading them sequentially can
            // deadlock when the child fills one pipe buffer while we're
            // blocked on the other.
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            try
            {
                await proc.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* best-effort */ }
                return (false, $"Install timed out after 5 minutes running '{fileName} {arguments}'. Try running it manually.");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var combined = string.IsNullOrWhiteSpace(stderr)
                ? stdout.Trim()
                : (stdout + "\n" + stderr).Trim();

            if (proc.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(combined)
                    ? $"exit code {proc.ExitCode}"
                    : combined;
                return (false, $"'{fileName} {arguments}' failed: {detail}");
            }
            return (true, combined);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
