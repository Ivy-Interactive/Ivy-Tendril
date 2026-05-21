using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;

namespace Ivy.Tendril.Agents.Providers.Codex;

public sealed class CodexHealthCheck : IAgentHealthCheck
{
    public string AgentId => Abstractions.AgentId.Codex;

    public async Task<AgentInstallStatus> CheckInstallAsync(CancellationToken ct = default)
    {
        var path = BinaryResolver.FindOnPath("codex");
        if (path is null)
            return new AgentInstallStatus { IsInstalled = false, Error = "codex not found on PATH" };

        var version = await GetVersionAsync(ct);
        return new AgentInstallStatus { IsInstalled = true, Version = version, BinaryPath = path };
    }

    public async Task<AgentAuthResult> CheckAuthAsync(CancellationToken ct = default)
    {
        var (exitCode, _, stderr) = await HealthCheckRunner.RunAsync(
            "codex",
            ["login", "status"],
            TimeSpan.FromSeconds(15),
            ct);

        if (exitCode == 0)
            return new AgentAuthResult { Status = AuthStatus.Authenticated };

        if (stderr.Contains("auth", StringComparison.OrdinalIgnoreCase) ||
            stderr.Contains("login", StringComparison.OrdinalIgnoreCase) ||
            stderr.Contains("not logged in", StringComparison.OrdinalIgnoreCase) ||
            stderr.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
            return new AgentAuthResult
            {
                Status = AuthStatus.NotAuthenticated,
                Error = stderr,
                SignInHint = "Run 'codex login' to authenticate",
            };

        return new AgentAuthResult { Status = AuthStatus.CheckFailed, Error = stderr };
    }

    public async Task<string?> GetVersionAsync(CancellationToken ct = default)
    {
        var (exitCode, stdout, _) = await HealthCheckRunner.RunAsync(
            "codex", ["--version"], TimeSpan.FromSeconds(10), ct);

        if (exitCode != 0) return null;
        return stdout.Trim();
    }

    public async Task<ModelValidationResult> ValidateModelAsync(string model, CancellationToken ct = default)
    {
        // Codex reads from stdin when '-' is specified, but HealthCheckRunner does not support stdin.
        // We rely on Codex validating the model flag at startup before waiting for input.
        var (exitCode, _, stderr) = await HealthCheckRunner.RunAsync(
            "codex",
            ["exec", "--full-auto", "--json", "--skip-git-repo-check", "--model", model, "-"],
            TimeSpan.FromSeconds(30),
            ct);

        if (exitCode == 0)
            return new ModelValidationResult { Status = ModelValidationStatus.Ok, Model = model };

        if (stderr.Contains("model", StringComparison.OrdinalIgnoreCase) &&
            (stderr.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
             stderr.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
             stderr.Contains("not supported", StringComparison.OrdinalIgnoreCase)))
            return new ModelValidationResult
            {
                Status = ModelValidationStatus.InvalidModel,
                Model = model,
                ErrorMessage = stderr,
            };

        if (stderr.Contains("auth", StringComparison.OrdinalIgnoreCase) ||
            stderr.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
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
        // Codex uses `codex login` which opens a browser.
        // Cannot drive this programmatically in a non-interactive context.
        return Task.FromResult(false);
    }

    public AgentOnboardingInfo GetOnboardingInfo() => new()
    {
        DisplayName = "Codex",
        InstallCommand = "npm install -g @openai/codex",
        InstallUrl = "https://github.com/openai/codex",
        AuthCommand = "codex login",
        SignInHint = "Run 'codex login' and complete the browser-based auth flow",
        DocsUrl = "https://github.com/openai/codex",
    };
}
