using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;
using Ivy.Tendril.Agents.Providers.Ivy;

namespace Ivy.Tendril.Agents.Providers.OpenAiProxy;

public sealed class OpenAiProxyHealthCheck : IAgentHealthCheck
{
    private readonly Func<string?> _apiKeyProvider;
    private readonly Func<string?> _baseUrlProvider;

    public OpenAiProxyHealthCheck(
        Func<string?>? apiKeyProvider = null,
        Func<string?>? baseUrlProvider = null)
    {
        _apiKeyProvider = apiKeyProvider ?? (() => null);
        _baseUrlProvider = baseUrlProvider ?? (() => null);
    }

    public string AgentId => Abstractions.AgentId.OpenAiProxy;

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

    public Task<AgentAuthResult> CheckAuthAsync(CancellationToken ct = default)
    {
        var baseUrl = _baseUrlProvider();
        var apiKey = _apiKeyProvider();

        if (string.IsNullOrEmpty(baseUrl))
        {
            return Task.FromResult(new AgentAuthResult
            {
                Status = AuthStatus.NotAuthenticated,
                Error = "Base URL is not configured.",
                SignInHint = "Specify the API Base URL under Settings -> Coding Agent."
            });
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            return Task.FromResult(new AgentAuthResult
            {
                Status = AuthStatus.NotAuthenticated,
                Error = "API Key is not configured.",
                SignInHint = "Specify an API Key under Settings -> Coding Agent."
            });
        }

        return Task.FromResult(new AgentAuthResult
        {
            Status = AuthStatus.Authenticated,
            Provider = "Configured Endpoint"
        });
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
        var baseUrl = _baseUrlProvider();
        var apiKey = _apiKeyProvider();

        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(apiKey))
        {
            return new ModelValidationResult
            {
                Status = ModelValidationStatus.AuthError,
                Model = model,
                ErrorMessage = "Base URL or API key not configured.",
            };
        }

        return await LlmEndpointTester.TestModelPromptAsync(baseUrl, apiKey, model, ct);
    }

    public Task<bool> RunAuthFlowAsync(AuthFlowCallbacks callbacks, CancellationToken ct = default)
        => Task.FromResult(false);

    public AgentOnboardingInfo GetOnboardingInfo() => new()
    {
        DisplayName = "OpenAI Proxy",
        InstallCommand = "curl -fsSL https://raw.githubusercontent.com/Ivy-Interactive/ivy-agent-cli/main/install.sh | bash",
        InstallUrl = "https://github.com/Ivy-Interactive/ivy-agent-cli",
        AuthCommand = "",
        SignInHint = "Enter your Base URL and API Key in settings",
        DocsUrl = "https://github.com/Ivy-Interactive/ivy-agent-cli",
    };
}
