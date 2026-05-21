using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;

namespace Ivy.Tendril.Agents.Providers.Claude;

public sealed class ClaudeHealthCheck : IAgentHealthCheck
{
    public string AgentId => Abstractions.AgentId.Claude;

    public async Task<AgentInstallStatus> CheckInstallAsync(CancellationToken ct = default)
    {
        var path = BinaryResolver.FindOnPath("claude");
        if (path is null)
            return new AgentInstallStatus { IsInstalled = false, Error = "claude not found on PATH" };

        var version = await GetVersionAsync(ct);
        return new AgentInstallStatus { IsInstalled = true, Version = version, BinaryPath = path };
    }

    public async Task<AgentAuthResult> CheckAuthAsync(CancellationToken ct = default)
    {
        var (exitCode, _, stderr) = await HealthCheckRunner.RunAsync(
            "claude",
            ["-p", "ping", "--max-turns", "1"],
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
                SignInHint = "Run 'claude login' to authenticate",
            };

        return new AgentAuthResult { Status = AuthStatus.CheckFailed, Error = stderr };
    }

    public async Task<string?> GetVersionAsync(CancellationToken ct = default)
    {
        var (exitCode, stdout, _) = await HealthCheckRunner.RunAsync(
            "claude", ["--version"], TimeSpan.FromSeconds(10), ct);

        if (exitCode != 0) return null;
        return stdout.Trim();
    }

    public async Task<ModelValidationResult> ValidateModelAsync(string model, CancellationToken ct = default)
    {
        var (exitCode, _, stderr) = await HealthCheckRunner.RunAsync(
            "claude",
            ["-p", "ping", "--model", model, "--max-turns", "1"],
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
        // Claude Code uses `claude login` which opens a browser.
        // We cannot drive this programmatically in a non-interactive context.
        return Task.FromResult(false);
    }

    public AgentOnboardingInfo GetOnboardingInfo() => new()
    {
        DisplayName = "Claude Code",
        InstallCommand = "npm install -g @anthropic-ai/claude-code",
        InstallUrl = "https://docs.anthropic.com/en/docs/claude-code",
        AuthCommand = "claude login",
        SignInHint = "Run 'claude login' and complete the browser-based auth flow",
        DocsUrl = "https://docs.anthropic.com/en/docs/claude-code",
    };
}
