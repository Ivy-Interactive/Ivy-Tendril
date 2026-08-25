using System.Runtime.InteropServices;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;

namespace Ivy.Tendril.Agents.Providers.Ivy;

public sealed class IvyHealthCheck : IAgentHealthCheck
{
    private readonly Func<string?> _apiKeyProvider;
    private readonly Func<string?> _tokenProvider;
    private readonly Func<CancellationToken, Task<string?>> _emailProvider;

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
        var path = IvyBinaryResolver.Resolve();
        if (!File.Exists(path))
        {
            path = await IvyBinaryResolver.EnsureInstalledAsync(ct) ?? path;
        }

        if (!File.Exists(path))
            return new AgentInstallStatus { IsInstalled = false, Error = "ivy-agent not found" };

        var version = await GetVersionAsync(ct);
        return new AgentInstallStatus { IsInstalled = true, Version = version, BinaryPath = path };
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
    {
        var binaryPath = IvyBinaryResolver.Resolve();
        var (exitCode, stdout, _) = await HealthCheckRunner.RunAsync(
            binaryPath, ["--version"], TimeSpan.FromSeconds(10), ct);

        if (exitCode != 0) return null;
        return stdout.Trim();
    }

    public async Task<ModelValidationResult> ValidateModelAsync(string model, CancellationToken ct = default)
    {
        var key = _apiKeyProvider();
        if (!string.IsNullOrEmpty(key))
        {
            return await LlmEndpointTester.TestModelPromptAsync("https://llmproxy.ivy.app/v1", key, model, ct);
        }

        var token = _tokenProvider();
        if (!string.IsNullOrEmpty(token))
        {
            var binaryPath = IvyBinaryResolver.Resolve();
            var originalEnv = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            var originalUrl = Environment.GetEnvironmentVariable("ANTHROPIC_BASE_URL");
            try
            {
                Environment.SetEnvironmentVariable("ANTHROPIC_BASE_URL", "https://llmproxy.ivy.app");

                var args = new List<string> { "run", "ping" };
                if (!string.IsNullOrEmpty(model) && !string.Equals(model, "default", StringComparison.OrdinalIgnoreCase))
                {
                    args.Add("--model");
                    args.Add(model);
                }

                var (exitCode, _, stderr) = await HealthCheckRunner.RunAsync(
                    binaryPath, args,
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
            finally
            {
                Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", originalEnv);
                Environment.SetEnvironmentVariable("ANTHROPIC_BASE_URL", originalUrl);
            }
        }

        return new ModelValidationResult
        {
            Status = ModelValidationStatus.AuthError,
            Model = model,
            ErrorMessage = "No API key or @ivy.app login configured.",
        };
    }

    public Task<bool> RunAuthFlowAsync(AuthFlowCallbacks callbacks, CancellationToken ct = default)
        => Task.FromResult(false);

    public AgentOnboardingInfo GetOnboardingInfo() => new()
    {
        DisplayName = "Ivy Agent",
        InstallCommand = "curl -fsSL https://raw.githubusercontent.com/Ivy-Interactive/ivy-agent-cli/main/install.sh | bash",
        InstallUrl = "https://github.com/Ivy-Interactive/ivy-agent-cli",
        AuthCommand = "",
        SignInHint = "Log in with an @ivy.app account or enter an API key in settings",
        DocsUrl = "https://github.com/Ivy-Interactive/ivy-agent-cli",
    };
}
