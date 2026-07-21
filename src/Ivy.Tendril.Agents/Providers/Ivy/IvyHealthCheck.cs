using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.OpenCode;

namespace Ivy.Tendril.Agents.Providers.Ivy;

public sealed class IvyHealthCheck : IAgentHealthCheck
{
    private readonly Func<string?> _apiKeyProvider;
    private readonly Func<string?> _tokenProvider;
    private readonly Func<CancellationToken, Task<string?>> _emailProvider;
    private readonly OpenCodeHealthCheck _inner = new();

    public IvyHealthCheck(
        Func<string?>? apiKeyProvider = null,
        Func<string?>? tokenProvider = null,
        Func<CancellationToken, Task<string?>>? emailProvider = null)
    {
        _apiKeyProvider = apiKeyProvider ?? (() => null);
        _tokenProvider = tokenProvider ?? (() => null);
        _emailProvider = emailProvider ?? ((_) => Task.FromResult<string?>(null));
    }

    public string AgentId => Abstractions.AgentId.Ivy;

    public async Task<AgentInstallStatus> CheckInstallAsync(CancellationToken ct = default)
    {
        var result = await _inner.CheckInstallAsync(ct);
        return new AgentInstallStatus
        {
            IsInstalled = result.IsInstalled,
            Version = result.Version,
            BinaryPath = result.BinaryPath,
            Error = result.Error?.Replace("opencode", "ivy")
        };
    }

    public async Task<AgentAuthResult> CheckAuthAsync(CancellationToken ct = default)
    {
        // 1. Check settings API key first
        var apiKey = _apiKeyProvider();
        if (!string.IsNullOrEmpty(apiKey))
        {
            return new AgentAuthResult { Status = AuthStatus.Authenticated, Provider = "API Key" };
        }

        // 2. Check main login token and email domain
        var token = _tokenProvider();
        if (!string.IsNullOrEmpty(token))
        {
            try
            {
                var email = await _emailProvider(ct);
                if (email != null)
                {
                    if (email.EndsWith("@ivy.app", StringComparison.OrdinalIgnoreCase))
                    {
                        return new AgentAuthResult { Status = AuthStatus.Authenticated, Provider = "@ivy.app Account" };
                    }
                    else
                    {
                        return new AgentAuthResult
                        {
                            Status = AuthStatus.NotAuthenticated,
                            Error = $"Logged in account '{email}' does not belong to the @ivy.app domain.",
                            SignInHint = "Log in with an @ivy.app account, or provide an API key in Settings -> Coding Agent."
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                return new AgentAuthResult
                {
                    Status = AuthStatus.CheckFailed,
                    Error = $"Failed to verify @ivy.app account status: {ex.Message}",
                    SignInHint = "Try logging in again or configure an API key."
                };
            }
        }

        return new AgentAuthResult
        {
            Status = AuthStatus.NotAuthenticated,
            Error = "No @ivy.app account logged in or API key configured.",
            SignInHint = "Please log in using an @ivy.app account, or specify an API key under settings."
        };
    }

    public async Task<string?> GetVersionAsync(CancellationToken ct = default)
        => await _inner.GetVersionAsync(ct);

    public async Task<ModelValidationResult> ValidateModelAsync(string model, CancellationToken ct = default)
    {
        var originalEnv = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        var originalUrl = Environment.GetEnvironmentVariable("ANTHROPIC_BASE_URL");
        try
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_BASE_URL", "https://llmproxy.ivy.app");
            var key = _apiKeyProvider();
            if (!string.IsNullOrEmpty(key))
            {
                Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", key);
            }
            
            var result = await _inner.ValidateModelAsync(model, ct);
            return new ModelValidationResult
            {
                Status = result.Status,
                Model = result.Model,
                ErrorMessage = result.ErrorMessage
            };
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", originalEnv);
            Environment.SetEnvironmentVariable("ANTHROPIC_BASE_URL", originalUrl);
        }
    }

    public Task<bool> RunAuthFlowAsync(AuthFlowCallbacks callbacks, CancellationToken ct = default)
        => Task.FromResult(false);

    public AgentOnboardingInfo GetOnboardingInfo() => new()
    {
        DisplayName = "Ivy Agent",
        InstallCommand = "npm install -g opencode-ai",
        InstallUrl = "https://opencode.ai",
        AuthCommand = "",
        SignInHint = "Log in with an @ivy.app account or enter an API key in settings",
        DocsUrl = "https://ivy.app",
    };
}
