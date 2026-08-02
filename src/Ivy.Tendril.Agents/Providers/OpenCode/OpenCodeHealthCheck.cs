using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;

namespace Ivy.Tendril.Agents.Providers.OpenCode;

public sealed class OpenCodeHealthCheck : IAgentHealthCheck
{
    public string AgentId => Abstractions.AgentId.OpenCode;

    public async Task<AgentInstallStatus> CheckInstallAsync(CancellationToken ct = default)
    {
        var path = OpenCodeBinaryResolver.Resolve();
        if (!File.Exists(path))
            return new AgentInstallStatus { IsInstalled = false, Error = "opencode not found" };

        var version = await GetVersionAsync(ct);
        return new AgentInstallStatus { IsInstalled = true, Version = version, BinaryPath = path };
    }

    public async Task<AgentAuthResult> CheckAuthAsync(CancellationToken ct = default)
    {
        var authPath = GetAuthFilePath();

        if (File.Exists(authPath))
        {
            var info = new FileInfo(authPath);
            if (info.Length >= 2)
            {
                return new AgentAuthResult
                {
                    Status = AuthStatus.Authenticated,
                    AuthMethod = "auth-file",
                };
            }
        }

        // Auth file not found or empty - check environment variables via CLI
        var binaryPath = OpenCodeBinaryResolver.Resolve();
        var (exitCode, stdout, stderr) = await HealthCheckRunner.RunAsync(
            binaryPath, ["auth", "list"], TimeSpan.FromSeconds(15), ct);

        if (exitCode != 0)
        {
            return new AgentAuthResult
            {
                Status = AuthStatus.Unknown,
                Error = $"Failed to check OpenCode credentials: {stderr}",
                SignInHint = "Run 'opencode providers login' to authenticate",
            };
        }

        var authResult = ParseAuthList(stdout);
        if (authResult.Status == AuthStatus.NotAuthenticated)
        {
            authResult = authResult with
            {
                Error = "No OpenCode credentials configured (auth.json empty and no provider environment variables)",
                SignInHint = "Run 'opencode providers login' to authenticate",
            };
        }

        return authResult;
    }

    internal static AgentAuthResult ParseAuthList(string stdout)
    {
        var cleaned = Regex.Replace(stdout, @"\x1b\[[0-9;]*m", "");

        var fileCredsMatch = Regex.Match(cleaned, @"(\d+)\s+credentials?", RegexOptions.IgnoreCase);
        var envCredsMatch = Regex.Match(cleaned, @"(\d+)\s+environment\s+variables?", RegexOptions.IgnoreCase);

        var fileCount = fileCredsMatch.Success ? int.Parse(fileCredsMatch.Groups[1].Value) : 0;
        var envCount = envCredsMatch.Success ? int.Parse(envCredsMatch.Groups[1].Value) : 0;

        if (envCount > 0)
        {
            return new AgentAuthResult
            {
                Status = AuthStatus.Authenticated,
                AuthMethod = "environment",
            };
        }

        if (fileCount > 0)
        {
            return new AgentAuthResult
            {
                Status = AuthStatus.Authenticated,
                AuthMethod = "auth-file",
            };
        }

        return new AgentAuthResult
        {
            Status = AuthStatus.NotAuthenticated,
        };
    }

    public async Task<string?> GetVersionAsync(CancellationToken ct = default)
    {
        var binaryPath = OpenCodeBinaryResolver.Resolve();
        var (exitCode, stdout, _) = await HealthCheckRunner.RunAsync(
            binaryPath, ["--version"], TimeSpan.FromSeconds(10), ct);

        if (exitCode != 0) return null;
        return stdout.Trim();
    }

    public async Task<ModelValidationResult> ValidateModelAsync(string model, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(model) && !string.Equals(model, "default", StringComparison.OrdinalIgnoreCase))
            return new ModelValidationResult
            {
                Status = ModelValidationStatus.Unknown,
                Model = model,
                ErrorMessage = "OpenCode does not support model validation for non-default models",
            };

        var binaryPath = OpenCodeBinaryResolver.Resolve();
        var (exitCode, _, stderr) = await HealthCheckRunner.RunAsync(
            binaryPath, ["run", "ping"],
            TimeSpan.FromSeconds(30), ct);

        if (exitCode == 0)
            return new ModelValidationResult { Status = ModelValidationStatus.Ok, Model = model };

        return new ModelValidationResult
        {
            Status = ModelValidationStatus.Unknown,
            Model = model,
            ErrorMessage = stderr,
        };
    }

    public Task<bool> RunAuthFlowAsync(AuthFlowCallbacks callbacks, CancellationToken ct = default)
    {
        return Task.FromResult(false);
    }

    public AgentOnboardingInfo GetOnboardingInfo() => new()
    {
        DisplayName = "OpenCode",
        InstallCommand = "npm install -g opencode-ai",
        InstallUrl = "https://opencode.ai",
        AuthCommand = "opencode providers login",
        SignInHint = "Run 'opencode providers login' to authenticate",
        DocsUrl = "https://opencode.ai",
    };

    private static string GetAuthFilePath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var xdgPath = Path.Combine(home, ".local", "share", "opencode", "auth.json");

        if (File.Exists(xdgPath))
            return xdgPath;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appDataPath = Path.Combine(appData, "opencode", "auth.json");
            if (File.Exists(appDataPath))
                return appDataPath;
        }

        return xdgPath;
    }
}
