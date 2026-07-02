using System.Runtime.InteropServices;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;

namespace Ivy.Tendril.Agents.Providers.OpenCode;

public sealed class OpenCodeHealthCheck : IAgentHealthCheck
{
    public string AgentId => Abstractions.AgentId.OpenCode;

    public async Task<AgentInstallStatus> CheckInstallAsync(CancellationToken ct = default)
    {
        var path = OpenCodeBinaryResolver.Resolve();
        if (!File.Exists(path))
            return new AgentInstallStatus { IsInstalled = false, Error = "opencode not found" };

        var version = await GetVersionAsync(ct);
        return new AgentInstallStatus { IsInstalled = true, Version = version, BinaryPath = path };
    }

    public Task<AgentAuthResult> CheckAuthAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new AgentAuthResult { Status = AuthStatus.Authenticated });
    }

    public async Task<string?> GetVersionAsync(CancellationToken ct = default)
    {
        var binaryPath = OpenCodeBinaryResolver.Resolve();
        var (exitCode, stdout, _) = await HealthCheckRunner.RunAsync(
            binaryPath, ["--version"], TimeSpan.FromSeconds(10), ct);

        if (exitCode != 0) return null;
        return stdout.Trim();
    }

    public async Task<ModelValidationResult> ValidateModelAsync(string model, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(model) && !string.Equals(model, "default", StringComparison.OrdinalIgnoreCase))
            return new ModelValidationResult
            {
                Status = ModelValidationStatus.Unknown,
                Model = model,
                ErrorMessage = "OpenCode does not support model validation for non-default models",
            };

        var binaryPath = OpenCodeBinaryResolver.Resolve();
        var (exitCode, _, stderr) = await HealthCheckRunner.RunAsync(
            binaryPath, ["run", "ping"],
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

    public Task<bool> RunAuthFlowAsync(AuthFlowCallbacks callbacks, CancellationToken ct = default)
    {
        return Task.FromResult(false);
    }

    public AgentOnboardingInfo GetOnboardingInfo() => new()
    {
        DisplayName = "OpenCode",
        InstallCommand = "npm install -g opencode-ai",
        InstallUrl = "https://opencode.ai",
        AuthCommand = "",
        SignInHint = "",
        DocsUrl = "https://opencode.ai",
    };
}
