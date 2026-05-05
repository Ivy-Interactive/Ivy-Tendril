using System.Diagnostics;

namespace Ivy.Tendril.Apps.Onboarding;

internal static class SoftwareInstaller
{
    public static bool CanAutoInstall(string key) => key switch
    {
        "powershell" => true,
        "gh" => OperatingSystem.IsWindows() || HasBrew(),
        "pandoc" => OperatingSystem.IsWindows() || HasBrew(),
        _ => false
    };

    public static Task<(bool Success, string Message)> InstallAsync(string key) => key switch
    {
        "powershell" => Run("dotnet", "tool install --global PowerShell"),
        "gh" => InstallPackage(brewPkg: "gh", wingetId: "GitHub.cli"),
        "pandoc" => InstallPackage(brewPkg: "pandoc", wingetId: "JohnMacFarlane.Pandoc"),
        _ => Task.FromResult<(bool, string)>((false, $"No installer configured for '{key}'."))
    };

    private static Task<(bool, string)> InstallPackage(string brewPkg, string wingetId)
    {
        if (OperatingSystem.IsWindows())
        {
            return Run("winget",
                $"install --id {wingetId} -e --silent --accept-source-agreements --accept-package-agreements");
        }

        var brew = ResolveBrew();
        if (brew is null)
        {
            return Task.FromResult<(bool, string)>(
                (false, "Homebrew not found. Install it from https://brew.sh and try again."));
        }

        return Run(brew, $"install {brewPkg}");
    }

    private static bool HasBrew() => ResolveBrew() is not null;

    private static string? ResolveBrew()
    {
        string[] candidates =
        {
            "/opt/homebrew/bin/brew",
            "/usr/local/bin/brew",
            "/home/linuxbrew/.linuxbrew/bin/brew"
        };
        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }
        return null;
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
