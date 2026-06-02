using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Commands.DoctorChecks;

internal class SoftwareCheck : IDoctorCheck
{
    private static readonly string[] RequiredSoftware = ["gh", "git"];

    private readonly ConfigService? _configService;
    private readonly IAgentRunner? _agentRunner;

    public SoftwareCheck(ConfigService? configService = null, IAgentRunner? agentRunner = null)
    {
        _configService = configService;
        _agentRunner = agentRunner;
    }

    public string Name => "Software";

    public async Task<CheckResult> RunAsync()
    {
        var statuses = new List<CheckStatus>();

        var reqErrors = await CheckRequiredSoftware(statuses);
        var agentErrors = await CheckAgentClis(statuses);
        var pwshErrors = await CheckPowerShell(statuses);

        return new CheckResult(reqErrors || agentErrors || pwshErrors, statuses);
    }

    private static async Task<bool> CheckRequiredSoftware(List<CheckStatus> statuses)
    {
        var hasErrors = false;
        foreach (var sw in RequiredSoftware)
        {
            var installed = await ProcessCheckHelper.CheckCommand(sw, "--version");
            if (!installed)
            {
                statuses.Add(new CheckStatus(sw, "Not found", StatusKind.Error));
                hasErrors = true;
                continue;
            }

            if (sw == "gh")
            {
                var health = await ProcessCheckHelper.CheckHealth(sw, "auth status --active");
                statuses.Add(health switch
                {
                    HealthCheckStatus.Authenticated => new CheckStatus(sw, "Ready", StatusKind.Ok),
                    HealthCheckStatus.NotAuthenticated => new CheckStatus(sw, "Installed but not authenticated", StatusKind.Error),
                    _ => new CheckStatus(sw, "Installed (health check failed)", StatusKind.Error),
                });
                if (health != HealthCheckStatus.Authenticated) hasErrors = true;
            }
            else
            {
                statuses.Add(new CheckStatus(sw, "OK", StatusKind.Ok));
            }
        }
        return hasErrors;
    }

    private async Task<bool> CheckAgentClis(List<CheckStatus> statuses)
    {
        if (_agentRunner == null) return false;

        var hasErrors = false;
        var codingAgent = _configService?.Settings.CodingAgent ?? "claude";
        var agentIds = GetAgentIds();

        foreach (var agentId in agentIds)
        {
            var isActive = agentId.Equals(codingAgent, StringComparison.OrdinalIgnoreCase);
            var healthCheck = _agentRunner.GetHealthCheck(agentId);
            var descriptor = _agentRunner.GetDescriptor(agentId);

            var installStatus = await healthCheck.CheckInstallAsync();
            if (!installStatus.IsInstalled)
            {
                statuses.Add(new CheckStatus(descriptor.DisplayName, "Not found", isActive ? StatusKind.Error : StatusKind.Warn));
                if (isActive) hasErrors = true;
                continue;
            }

            var authResult = await healthCheck.CheckAuthAsync();
            var (message, kind) = authResult.Status switch
            {
                AuthStatus.Authenticated => ($"Ready ({installStatus.Version ?? "installed"})", StatusKind.Ok),
                AuthStatus.NotAuthenticated => ("Installed but not authenticated", isActive ? StatusKind.Error : StatusKind.Warn),
                _ => ("Installed (health check failed)", isActive ? StatusKind.Error : StatusKind.Warn),
            };
            statuses.Add(new CheckStatus(descriptor.DisplayName, message, kind));
            if (authResult.Status != AuthStatus.Authenticated && isActive) hasErrors = true;
        }
        return hasErrors;
    }

    private static async Task<bool> CheckPowerShell(List<CheckStatus> statuses)
    {
        var pwshInstalled = await ProcessCheckHelper.CheckCommand("pwsh", "-Version");
        if (pwshInstalled)
        {
            statuses.Add(new CheckStatus("powershell", "OK (pwsh)", StatusKind.Ok));
            return false;
        }

        var legacyInstalled = await ProcessCheckHelper.CheckCommand("powershell", "-Version");
        statuses.Add(legacyInstalled
            ? new CheckStatus("powershell", "OK (powershell)", StatusKind.Ok)
            : new CheckStatus("powershell", "Not found", StatusKind.Error));
        return !legacyInstalled;
    }

    private string[] GetAgentIds()
    {
        if (_agentRunner == null) return [];
        return _agentRunner.RegisteredAgents.ToArray();
    }
}
