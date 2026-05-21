using System.Runtime.InteropServices;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;

namespace Ivy.Tendril.Agents.Providers.OpenCode;

public sealed class OpenCodeHealthCheck : IAgentHealthCheck
{
    public string AgentId => Abstractions.AgentId.OpenCode;

    public async Task<AgentInstallStatus> CheckInstallAsync(CancellationToken ct = default)
    {
        var path = BinaryResolver.FindOnPath("opencode");
        if (path is null)
            return new AgentInstallStatus { IsInstalled = false, Error = "opencode not found on PATH" };

        var version = await GetVersionAsync(ct);
        return new AgentInstallStatus { IsInstalled = true, Version = version, BinaryPath = path };
    }

    public Task<AgentAuthResult> CheckAuthAsync(CancellationToken ct = default)
    {
        var authPath = GetAuthFilePath();

        if (File.Exists(authPath))
        {
            var info = new FileInfo(authPath);
            if (info.Length >= 2)
            {
                return Task.FromResult(new AgentAuthResult { Status = AuthStatus.Authenticated });
            }
        }

        return Task.FromResult(new AgentAuthResult
        {
            Status = AuthStatus.NotAuthenticated,
            Error = $"Auth file not found or empty at {authPath}",
            SignInHint = "Run 'opencode providers login' to authenticate",
        });
    }

    public async Task<string?> GetVersionAsync(CancellationToken ct = default)
    {
        var (exitCode, stdout, _) = await HealthCheckRunner.RunAsync(
            "opencode", ["--version"], TimeSpan.FromSeconds(10), ct);

        if (exitCode != 0) return null;
        return stdout.Trim();
    }

    public Task<ModelValidationResult> ValidateModelAsync(string model, CancellationToken ct = default)
    {
        return Task.FromResult(new ModelValidationResult
        {
            Status = ModelValidationStatus.Unknown,
            Model = model,
            ErrorMessage = "OpenCode does not provide a model validation command",
        });
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
