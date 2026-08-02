using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Commands.DoctorChecks;

internal class AgentModelsCheck(ConfigService? configService = null, IAgentRunner? agentRunner = null) : IDoctorCheck
{
    public string Name => "Agent Models";

    public async Task<CheckResult> RunAsync(CancellationToken ct = default)
    {
        if (configService?.Settings == null || agentRunner == null)
            return new CheckResult(false, []);

        var codingAgent = configService.Settings.CodingAgent ?? "claude";
        var configuredAgents = configService.Settings.CodingAgents ?? [];

        // Probe all agents concurrently
        var tasks = agentRunner.RegisteredAgents
            .Select(agentId => ProbeAgentModelsAsync(agentId, codingAgent, configuredAgents, ct))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        // Concatenate statuses in registration order
        var statuses = new List<CheckStatus>();
        var hasErrors = false;
        foreach (var (agentStatuses, agentHasErrors) in results)
        {
            statuses.AddRange(agentStatuses);
            if (agentHasErrors) hasErrors = true;
        }

        return new CheckResult(hasErrors, statuses);
    }

    private async Task<(List<CheckStatus> Statuses, bool HasErrors)> ProbeAgentModelsAsync(
        string agentId, string codingAgent, List<Services.AgentConfig> configuredAgents, CancellationToken ct)
    {
        var statuses = new List<CheckStatus>();
        var hasErrors = false;

        var isActive = agentId.Equals(codingAgent, StringComparison.OrdinalIgnoreCase);
        var label = isActive ? $"{agentId} (active)" : agentId;

        var healthCheck = agentRunner!.GetHealthCheck(agentId);
        var installStatus = await healthCheck.CheckInstallAsync(ct);

        if (!installStatus.IsInstalled)
        {
            statuses.Add(new CheckStatus(label, "CLI not found — skipping", StatusKind.Warn));
            return (statuses, hasErrors);
        }

        statuses.Add(new CheckStatus(label, "", StatusKind.Ok));

        // Use explicit config profiles if available, otherwise use default profiles
        var agentConfig = configuredAgents.FirstOrDefault(a =>
            AgentProviderFactory.NormalizeAgentName(a.Name).Equals(agentId, StringComparison.OrdinalIgnoreCase));

        // Keep cache per-agent to dedup repeated models within one agent (must stay sequential to avoid races)
        var validatedModels = new Dictionary<string, ModelValidationResult>(StringComparer.OrdinalIgnoreCase);

        if (agentConfig is { Profiles.Count: > 0 })
        {
            foreach (var profile in agentConfig.Profiles)
            {
                var model = string.IsNullOrEmpty(profile.Model) ? "default" : profile.Model;
                if (!validatedModels.TryGetValue(model, out var result))
                {
                    result = await healthCheck.ValidateModelAsync(model, ct);
                    validatedModels[model] = result;
                }

                var profileLabel = $"  {profile.Name}: {model}";
                AddProfileStatus(statuses, profileLabel, result, ref hasErrors);
            }
        }
        else
        {
            var descriptor = agentRunner.GetCli(agentId);
            foreach (var profile in descriptor.DefaultProfiles)
            {
                var model = profile.Model ?? "default";
                if (!validatedModels.TryGetValue(model, out var result))
                {
                    result = await healthCheck.ValidateModelAsync(model, ct);
                    validatedModels[model] = result;
                }

                var profileLabel = $"  {profile.Tier.ToString().ToLowerInvariant()}: {model}";
                AddProfileStatus(statuses, profileLabel, result, ref hasErrors);
            }
        }

        return (statuses, hasErrors);
    }

    private static void AddProfileStatus(List<CheckStatus> statuses, string label, ModelValidationResult result, ref bool hasErrors)
    {
        switch (result.Status)
        {
            case ModelValidationStatus.Ok:
                statuses.Add(new CheckStatus(label, "OK", StatusKind.Ok));
                break;
            case ModelValidationStatus.InvalidModel:
                statuses.Add(new CheckStatus(label, result.ErrorMessage ?? "Invalid model ID", StatusKind.Error));
                hasErrors = true;
                break;
            case ModelValidationStatus.AuthError:
                statuses.Add(new CheckStatus(label, result.ErrorMessage ?? "Auth error", StatusKind.Error));
                hasErrors = true;
                break;
            case ModelValidationStatus.Timeout:
                statuses.Add(new CheckStatus(label, "Timeout", StatusKind.Warn));
                break;
            case ModelValidationStatus.Unknown:
                statuses.Add(new CheckStatus(label, result.ErrorMessage ?? "Check failed", StatusKind.Warn));
                break;
        }
    }
}
