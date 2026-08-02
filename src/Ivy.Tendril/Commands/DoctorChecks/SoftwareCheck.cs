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

    public async Task<CheckResult> RunAsync(CancellationToken ct = default)
    {
        var statuses = new List<CheckStatus>();

        // Run all check groups concurrently, each building its own status list
        var reqTask = CheckRequiredSoftware(ct);
        var agentTask = CheckAgentClis(ct);
        var pwshTask = CheckPowerShell(ct);
        var dotnetTask = CheckDotNet(ct);

        await Task.WhenAll(reqTask, agentTask, pwshTask, dotnetTask);

        // Concatenate results in the existing order: required software, agents, powershell, dotnet
        var reqResult = await reqTask;
        var agentResult = await agentTask;
        var pwshResult = await pwshTask;
        var dotnetResult = await dotnetTask;

        statuses.AddRange(reqResult.Statuses);
        statuses.AddRange(agentResult.Statuses);
        statuses.AddRange(pwshResult.Statuses);
        statuses.AddRange(dotnetResult.Statuses);

        var hasErrors = reqResult.HasErrors || agentResult.HasErrors || pwshResult.HasErrors || dotnetResult.HasErrors;
        return new CheckResult(hasErrors, statuses);
    }

    private static async Task<CheckResult> CheckRequiredSoftware(CancellationToken ct)
    {
        var statuses = new List<CheckStatus>();
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
        return new CheckResult(hasErrors, statuses);
    }

    private async Task<CheckResult> CheckAgentClis(CancellationToken ct)
    {
        if (_agentRunner == null) return new CheckResult(false, []);

        var codingAgent = _configService?.Settings.CodingAgent ?? "claude";
        var agentIds = GetAgentIds();

        // Probe all agents concurrently
        var tasks = agentIds.Select(agentId => ProbeAgentAsync(agentId, codingAgent, ct)).ToArray();
        var results = await Task.WhenAll(tasks);

        // Concatenate statuses in registration order
        var statuses = new List<CheckStatus>();
        var hasErrors = false;
        foreach (var (status, hasError) in results)
        {
            statuses.AddRange(status);
            if (hasError) hasErrors = true;
        }

        return new CheckResult(hasErrors, statuses);
    }

    private async Task<(List<CheckStatus> Statuses, bool HasErrors)> ProbeAgentAsync(string agentId, string codingAgent, CancellationToken ct)
    {
        var statuses = new List<CheckStatus>();
        var hasErrors = false;
        var isActive = agentId.Equals(codingAgent, StringComparison.OrdinalIgnoreCase);
        var healthCheck = _agentRunner!.GetHealthCheck(agentId);
        var descriptor = _agentRunner.GetDescriptor(agentId);

        var installStatus = await healthCheck.CheckInstallAsync(ct);
        if (!installStatus.IsInstalled)
        {
            statuses.Add(new CheckStatus(descriptor.DisplayName, "Not found", isActive ? StatusKind.Error : StatusKind.Warn));
            if (isActive) hasErrors = true;
            return (statuses, hasErrors);
        }

        var authResult = await healthCheck.CheckAuthAsync(ct);
        var (message, kind) = authResult.Status switch
        {
            AuthStatus.Authenticated => ($"Ready ({installStatus.Version ?? "installed"})", StatusKind.Ok),
            AuthStatus.NotAuthenticated => ("Installed but not authenticated", isActive ? StatusKind.Error : StatusKind.Warn),
            _ => ("Installed (health check failed)", isActive ? StatusKind.Error : StatusKind.Warn),
        };
        statuses.Add(new CheckStatus(descriptor.DisplayName, message, kind));
        if (authResult.Status != AuthStatus.Authenticated && isActive) hasErrors = true;

        return (statuses, hasErrors);
    }

    private static async Task<CheckResult> CheckPowerShell(CancellationToken ct)
    {
        var statuses = new List<CheckStatus>();
        var bundledPath = PathHelper.GetPwshPath();
        var (success, error) = await ProcessCheckHelper.CheckPowerShellWithDetails();
        if (success)
        {
            // Determine if the working PowerShell is the bundled one
            var isBundled = bundledPath != "pwsh" && await ProcessCheckHelper.CheckCommand(bundledPath, "-Version");
            statuses.Add(new CheckStatus("powershell", isBundled ? "OK (bundled pwsh)" : "OK (pwsh)", StatusKind.Ok));
            return new CheckResult(false, statuses);
        }

        var errorMessage = $"Not found or failed to execute. Details: {error}";
        statuses.Add(new CheckStatus("powershell", errorMessage, StatusKind.Error));
        return new CheckResult(true, statuses);
    }

    private static async Task<CheckResult> CheckDotNet(CancellationToken ct)
    {
        var statuses = new List<CheckStatus>();
        var bundledPath = PathHelper.GetBundledDotnetPath();

        // Try bundled first if it exists
        if (bundledPath != null)
        {
            var (success, err) = await ProcessCheckHelper.TryCheckCommand(bundledPath, "--version");
            if (success)
            {
                statuses.Add(new CheckStatus("dotnet", "OK (bundled dotnet)", StatusKind.Ok));
                return new CheckResult(false, statuses);
            }
        }

        // Try system dotnet next
        var (sysSuccess, sysErr) = await ProcessCheckHelper.TryCheckCommand("dotnet", "--version");
        if (sysSuccess)
        {
            statuses.Add(new CheckStatus("dotnet", "OK (dotnet)", StatusKind.Ok));
            return new CheckResult(false, statuses);
        }

        var details = bundledPath != null ? $"bundled: {bundledPath} failed, system: {sysErr}" : sysErr;
        statuses.Add(new CheckStatus("dotnet", $"Not found or failed to execute. Details: {details}", StatusKind.Error));
        return new CheckResult(true, statuses);
    }

    private string[] GetAgentIds()
    {
        if (_agentRunner == null) return [];
        return _agentRunner.RegisteredAgents.ToArray();
    }
}
