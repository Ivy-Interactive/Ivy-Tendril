using System.Diagnostics;
using System.Text.Json;

namespace Ivy.Tendril.Helpers;

public static class GitHubCliHelper
{
    private static async Task<string[]> RunCommandAsync(string arguments)
    {
        try
        {
            var isWindows = OperatingSystem.IsWindows();
            var shell = isWindows ? "pwsh" : "pwsh"; // Using pwsh on all platforms as requested by assumption
            var psi = new ProcessStartInfo
            {
                FileName = shell,
                Arguments = $"-NoProfile -Command \"{arguments}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return [];

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                return [];
            }

            return output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }
        catch
        {
            return [];
        }
    }

    public static async Task<string[]> GetOwnersAsync()
    {
        // Get both user and organizations
        var cmd = "gh api graphql -F query='query { viewer { login organizations(first:100) { nodes { login } } } }' --jq '.data.viewer.login, .data.viewer.organizations.nodes[].login'";
        return await RunCommandAsync(cmd);
    }

    public static async Task<string[]> GetRepositoriesAsync(string owner)
    {
        // Ensure owner string doesn't contain malicious quotes
        var safeOwner = owner.Replace("'", "").Replace("\"", "");
        var cmd = $"gh repo list {safeOwner} --json name --jq '.[].name' -L 100";
        return await RunCommandAsync(cmd);
    }

    public static async Task<string[]> GetBranchesAsync(string owner, string repo)
    {
        var safeOwner = owner.Replace("'", "").Replace("\"", "");
        var safeRepo = repo.Replace("'", "").Replace("\"", "");
        var cmd = $"gh api repos/{safeOwner}/{safeRepo}/branches --jq '.[].name'";
        return await RunCommandAsync(cmd);
    }

    public static async Task<string?> GetDefaultBranchAsync(string owner, string repo)
    {
        var safeOwner = owner.Replace("'", "").Replace("\"", "");
        var safeRepo = repo.Replace("'", "").Replace("\"", "");
        var cmd = $"gh api repos/{safeOwner}/{safeRepo} --jq '.default_branch'";
        var result = await RunCommandAsync(cmd);
        return result.Length > 0 ? result[0] : null;
    }

    public static async Task<bool> CloneRepositoryAsync(string url, string destinationPath)
    {
        try
        {
            var isWindows = OperatingSystem.IsWindows();
            var shell = isWindows ? "pwsh" : "pwsh";

            // Validate arguments somewhat to prevent injection
            if (url.Contains('\'') || url.Contains('"')) return false;

            string cmd;
            if (System.IO.Directory.Exists(destinationPath))
            {
                cmd = $"git -C '{destinationPath}' pull";
            }
            else
            {
                cmd = $"git clone '{url}' '{destinationPath}'";
            }

            var psi = new ProcessStartInfo
            {
                FileName = shell,
                Arguments = $"-NoProfile -Command \"{cmd}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
