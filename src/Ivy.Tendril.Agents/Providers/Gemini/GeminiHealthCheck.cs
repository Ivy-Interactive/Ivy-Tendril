using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;

namespace Ivy.Tendril.Agents.Providers.Gemini;

public sealed class GeminiHealthCheck : IAgentHealthCheck
{
    public string AgentId => Abstractions.AgentId.Gemini;

    public async Task<AgentInstallStatus> CheckInstallAsync(CancellationToken ct = default)
    {
        var path = BinaryResolver.FindOnPath("gemini");
        if (path is null)
            return new AgentInstallStatus { IsInstalled = false, Error = "gemini not found on PATH" };

        var version = await GetVersionAsync(ct);
        return new AgentInstallStatus { IsInstalled = true, Version = version, BinaryPath = path };
    }

    public Task<AgentAuthResult> CheckAuthAsync(CancellationToken ct = default)
    {
        var apiKey = Environment.GetEnvironmentVariable("GOOGLE_API_KEY")
                  ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");

        if (!string.IsNullOrEmpty(apiKey))
        {
            return Task.FromResult(new AgentAuthResult
            {
                Status = AuthStatus.Authenticated,
                AuthMethod = "api-key",
            });
        }

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
            Error = "OAuth credentials not found and no API key set",
            SignInHint = "Run 'gemini auth' or set GEMINI_API_KEY",
        });
    }

    public async Task<string?> GetVersionAsync(CancellationToken ct = default)
    {
        var (exitCode, stdout, _) = await HealthCheckRunner.RunAsync(
            "gemini", ["--version"], TimeSpan.FromSeconds(10), ct);

        if (exitCode != 0) return null;
        return stdout.Trim();
    }

    public Task<ModelValidationResult> ValidateModelAsync(string model, CancellationToken ct = default)
    {
        // Gemini CLI doesn't support lightweight model validation — a full prompt invocation
        // is too slow for a health check. Accept all models; invalid ones will fail at runtime.
        return Task.FromResult(new ModelValidationResult { Status = ModelValidationStatus.Ok, Model = model });
    }

    public Task<bool> RunAuthFlowAsync(AuthFlowCallbacks callbacks, CancellationToken ct = default)
    {
        return Task.FromResult(false);
    }

    public AgentOnboardingInfo GetOnboardingInfo() => new()
    {
        DisplayName = "Gemini CLI",
        InstallCommand = "npm install -g @google/gemini-cli",
        InstallUrl = "https://github.com/google-gemini/gemini-cli",
        AuthCommand = "gemini auth",
        SignInHint = "Run 'gemini auth' to authenticate via browser",
        DocsUrl = "https://github.com/google-gemini/gemini-cli",
    };
}
