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

        var provider = DetectProvider();

        if (exitCode == 0)
            return new AgentAuthResult { Status = AuthStatus.Authenticated, Provider = provider };

        if (stderr.Contains("auth", StringComparison.OrdinalIgnoreCase) ||
            stderr.Contains("login", StringComparison.OrdinalIgnoreCase) ||
            stderr.Contains("sign in", StringComparison.OrdinalIgnoreCase))
            return new AgentAuthResult
            {
                Status = AuthStatus.NotAuthenticated,
                Provider = provider,
                Error = stderr,
                SignInHint = "Run 'claude login' to authenticate",
            };

        return new AgentAuthResult { Status = AuthStatus.CheckFailed, Provider = provider, Error = stderr };
    }

    private static string? DetectProvider()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CLAUDE_CODE_USE_BEDROCK")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AWS_BEARER_TOKEN_BEDROCK")))
            return "bedrock";

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CLAUDE_CODE_USE_VERTEX")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CLOUD_ML_REGION")))
            return "vertex";

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")))
            return "anthropic-api";

        return null;
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
        var args = string.IsNullOrEmpty(model)
            ? (IReadOnlyList<string>)["-p", "ping", "--max-turns", "1"]
            : ["-p", "ping", "--model", model, "--max-turns", "1"];

        var (exitCode, stdout, stderr) = await HealthCheckRunner.RunAsync(
            "claude", args, TimeSpan.FromSeconds(30), ct);

        if (exitCode == 0)
            return new ModelValidationResult { Status = ModelValidationStatus.Ok, Model = model };

        var combined = string.IsNullOrEmpty(stderr) ? stdout : stderr;

        if (combined.Contains("model", StringComparison.OrdinalIgnoreCase) &&
            (combined.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
             combined.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
             combined.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
             combined.Contains("not supported", StringComparison.OrdinalIgnoreCase) ||
             combined.Contains("not available", StringComparison.OrdinalIgnoreCase)))
            return new ModelValidationResult
            {
                Status = ModelValidationStatus.InvalidModel,
                Model = model,
                ErrorMessage = combined,
            };

        if (combined.Contains("auth", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
            return new ModelValidationResult
            {
                Status = ModelValidationStatus.AuthError,
                Model = model,
                ErrorMessage = combined,
            };

        var fullOutput = $"exit={exitCode}\nstdout: {stdout}\nstderr: {stderr}";
        return new ModelValidationResult
        {
            Status = ModelValidationStatus.Unknown,
            Model = model,
            ErrorMessage = fullOutput,
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
