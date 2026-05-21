using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;

namespace Ivy.Tendril.Agents.Providers.Copilot;

public sealed class CopilotHealthCheck : IAgentHealthCheck
{
    public string AgentId => Abstractions.AgentId.Copilot;

    public async Task<AgentInstallStatus> CheckInstallAsync(CancellationToken ct = default)
    {
        var path = BinaryResolver.FindOnPath("copilot");
        if (path is null)
            return new AgentInstallStatus { IsInstalled = false, Error = "copilot not found on PATH" };

        var version = await GetVersionAsync(ct);
        return new AgentInstallStatus { IsInstalled = true, Version = version, BinaryPath = path };
    }

    public async Task<AgentAuthResult> CheckAuthAsync(CancellationToken ct = default)
    {
        var (exitCode, _, stderr) = await HealthCheckRunner.RunAsync(
            "copilot",
            ["-p", "ping", "--allow-all-paths", "--allow-all-urls", "--allow-all-tools", "-s"],
            TimeSpan.FromSeconds(30),
            ct);

        if (exitCode == 0)
            return new AgentAuthResult { Status = AuthStatus.Authenticated };

        if (stderr.Contains("auth", StringComparison.OrdinalIgnoreCase) ||
            stderr.Contains("login", StringComparison.OrdinalIgnoreCase) ||
            stderr.Contains("sign in", StringComparison.OrdinalIgnoreCase))
            return new AgentAuthResult
            {
                Status = AuthStatus.NotAuthenticated,
                Error = stderr,
                SignInHint = "Run 'copilot login' to authenticate",
            };

        return new AgentAuthResult { Status = AuthStatus.CheckFailed, Error = stderr };
    }

    public async Task<string?> GetVersionAsync(CancellationToken ct = default)
    {
        var (exitCode, stdout, _) = await HealthCheckRunner.RunAsync(
            "copilot", ["--version"], TimeSpan.FromSeconds(10), ct);

        if (exitCode != 0) return null;
        return stdout.Trim();
    }

    public async Task<ModelValidationResult> ValidateModelAsync(string model, CancellationToken ct = default)
    {
        var (exitCode, _, stderr) = await HealthCheckRunner.RunAsync(
            "copilot",
            ["-p", "ping", "--model", model, "--allow-all-paths", "--allow-all-urls", "--allow-all-tools", "-s"],
            TimeSpan.FromSeconds(30),
            ct);

        if (exitCode == 0)
            return new ModelValidationResult { Status = ModelValidationStatus.Ok, Model = model };

        if (stderr.Contains("model", StringComparison.OrdinalIgnoreCase) &&
            (stderr.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
             stderr.Contains("not found", StringComparison.OrdinalIgnoreCase)))
            return new ModelValidationResult
            {
                Status = ModelValidationStatus.InvalidModel,
                Model = model,
                ErrorMessage = stderr,
            };

        if (stderr.Contains("auth", StringComparison.OrdinalIgnoreCase))
            return new ModelValidationResult
            {
                Status = ModelValidationStatus.AuthError,
                Model = model,
                ErrorMessage = stderr,
            };

        return new ModelValidationResult
        {
            Status = ModelValidationStatus.Unknown,
            Model = model,
            ErrorMessage = stderr,
        };
    }

    public Task<bool> RunAuthFlowAsync(AuthFlowCallbacks callbacks, CancellationToken ct = default)
    {
        // Copilot uses `copilot login` which opens a browser-based auth flow.
        // Cannot be driven programmatically in a non-interactive context.
        return Task.FromResult(false);
    }

    public AgentOnboardingInfo GetOnboardingInfo() => new()
    {
        DisplayName = "GitHub Copilot",
        InstallCommand = "npm install -g @githubnext/copilot-cli",
        InstallUrl = "https://docs.github.com/en/copilot",
        AuthCommand = "copilot login",
        SignInHint = "Run 'copilot login' and complete the browser-based auth flow",
        DocsUrl = "https://docs.github.com/en/copilot",
    };
}
