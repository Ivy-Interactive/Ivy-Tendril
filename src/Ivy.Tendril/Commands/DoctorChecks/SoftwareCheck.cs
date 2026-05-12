using Ivy.Tendril.Services;

namespace Ivy.Tendril.Commands.DoctorChecks;

internal class SoftwareCheck : IDoctorCheck
{
    private static readonly string[] RequiredSoftware = ["gh", "git"];
    private static readonly string[] OptionalSoftware = ["pandoc"];

    private static readonly Dictionary<string, string> VersionArgs = new()
    {
        ["gh"] = "--version",
        ["claude"] = "--version",
        ["codex"] = "--version",
        ["gemini"] = "--version",
        ["copilot"] = "--version",
        ["git"] = "--version",
        ["pwsh"] = "-Version",
        ["pandoc"] = "--version"
    };

    private static readonly Dictionary<string, string> HealthArgs = new()
    {
        ["gh"] = "auth status --active",
        ["claude"] = "-p \"ping\" --max-turns 1",
        ["codex"] = "login status",
        ["copilot"] = "-p \"ping\" --allow-all -s"
    };

    private readonly ConfigService? _configService;

    public SoftwareCheck(ConfigService? configService = null)
    {
        _configService = configService;
    }

    public string Name => "Software";

    public async Task<CheckResult> RunAsync()
    {
        var statuses = new List<CheckStatus>();
        var hasErrors = false;

        var codingAgent = _configService?.Settings.CodingAgent ?? "claude";
        var agentClis = GetAgentClis(_configService);

        var allSoftware = RequiredSoftware
            .Concat(agentClis)
            .Concat(OptionalSoftware)
            .Distinct()
            .ToList();

        foreach (var sw in allSoftware)
        {
            var isRequired = RequiredSoftware.Contains(sw) || agentClis.Contains(sw);
            var versionArg = VersionArgs.GetValueOrDefault(sw, "--version");
            var installed = await ProcessHelper.CheckCommand(sw, versionArg);

            if (!installed)
            {
                var kind = isRequired ? StatusKind.Error : StatusKind.Warn;
                statuses.Add(new CheckStatus(sw, "Not found", kind));
                if (isRequired) hasErrors = true;
                continue;
            }

            if (HealthArgs.TryGetValue(sw, out var healthArg))
            {
                var health = await ProcessHelper.CheckHealth(sw, healthArg);
                switch (health)
                {
                    case HealthResult.Authenticated:
                        statuses.Add(new CheckStatus(sw, "Ready", StatusKind.Ok));
                        break;
                    case HealthResult.NotAuthenticated:
                        statuses.Add(new CheckStatus(sw, "Installed but not authenticated", isRequired ? StatusKind.Error : StatusKind.Warn));
                        if (isRequired) hasErrors = true;
                        break;
                    default:
                        statuses.Add(new CheckStatus(sw, "Installed (health check failed)", isRequired ? StatusKind.Error : StatusKind.Warn));
                        if (isRequired) hasErrors = true;
                        break;
                }
            }
            else
            {
                statuses.Add(new CheckStatus(sw, "OK", StatusKind.Ok));
            }
        }

        // PowerShell check with fallback (pwsh → powershell)
        var pwshInstalled = await ProcessHelper.CheckCommand("pwsh", "-Version");
        if (pwshInstalled)
        {
            statuses.Add(new CheckStatus("powershell", "OK (pwsh)", StatusKind.Ok));
        }
        else
        {
            var legacyInstalled = await ProcessHelper.CheckCommand("powershell", "-Version");
            if (legacyInstalled)
            {
                statuses.Add(new CheckStatus("powershell", "OK (powershell)", StatusKind.Ok));
            }
            else
            {
                statuses.Add(new CheckStatus("powershell", "Not found", StatusKind.Error));
                hasErrors = true;
            }
        }

        return new CheckResult(hasErrors, statuses);
    }

    private static string[] GetAgentClis(ConfigService? configService)
    {
        var codingAgent = configService?.Settings.CodingAgent ?? "claude";
        var clis = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (configService?.Settings.CodingAgents is { Count: > 0 } agents)
        {
            foreach (var agent in agents)
            {
                var cli = ResolveCliName(agent.Name, codingAgent);
                if (cli != null) clis.Add(cli);
            }
        }
        else
        {
            clis.Add(codingAgent);
        }

        return clis.ToArray();
    }

    private static string? ResolveCliName(string agentName, string codingAgent)
    {
        return agentName.ToLower() switch
        {
            "claude" or "claudecode" => "claude",
            "codex" => "codex",
            "gemini" => "gemini",
            "copilot" => "copilot",
            _ => null
        };
    }
}
