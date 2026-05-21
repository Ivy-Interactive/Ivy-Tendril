using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;

namespace Ivy.Tendril.Agents.Providers.Antigravity;

public sealed class AntigravityHealthCheck : IAgentHealthCheck
{
    public string AgentId => Abstractions.AgentId.Antigravity;

    public async Task<AgentInstallStatus> CheckInstallAsync(CancellationToken ct = default)
    {
        var path = BinaryResolver.FindOnPath("agy");
        if (path is null)
            return new AgentInstallStatus { IsInstalled = false, Error = "agy not found on PATH" };

        var version = await GetVersionAsync(ct);
        return new AgentInstallStatus { IsInstalled = true, Version = version, BinaryPath = path };
    }

    public Task<AgentAuthResult> CheckAuthAsync(CancellationToken ct = default)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var credPath = Path.Combine(home, ".gemini", "oauth_creds.json");

        if (File.Exists(credPath))
        {
            var info = new FileInfo(credPath);
            if (info.Length > 0)
                return Task.FromResult(new AgentAuthResult
                {
                    Status = AuthStatus.Authenticated,
                    AuthMethod = "oauth",
                });
        }

        return Task.FromResult(new AgentAuthResult
        {
            Status = AuthStatus.NotAuthenticated,
            Error = "OAuth credentials file not found",
            SignInHint = "Run 'agy' and complete the browser-based auth flow",
        });
    }

    public async Task<string?> GetVersionAsync(CancellationToken ct = default)
    {
        var (exitCode, stdout, _) = await HealthCheckRunner.RunAsync(
            "agy", ["--version"], TimeSpan.FromSeconds(10), ct);

        if (exitCode != 0) return null;
        return stdout.Trim();
    }

    public Task<ModelValidationResult> ValidateModelAsync(string model, CancellationToken ct = default)
    {
        return Task.FromResult(new ModelValidationResult
        {
            Status = ModelValidationStatus.Unknown,
            Model = model,
            ErrorMessage = "Model validation not supported via Antigravity CLI",
        });
    }

    public Task<bool> RunAuthFlowAsync(AuthFlowCallbacks callbacks, CancellationToken ct = default)
    {
        return Task.FromResult(false);
    }

    public AgentOnboardingInfo GetOnboardingInfo() => new()
    {
        DisplayName = "Antigravity",
        InstallCommand = "agy install",
        InstallUrl = "https://antigravity.dev",
        AuthCommand = "agy",
        SignInHint = "Run 'agy' and complete the browser-based auth flow",
        DocsUrl = "https://antigravity.dev",
    };
}
