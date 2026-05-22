using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Commands.DoctorChecks;

internal class AgentModelsCheck(ConfigService? configService = null, IAgentRunner? agentRunner = null) : IDoctorCheck
{
    public string Name => "Agent Models";

    public async Task<CheckResult> RunAsync()
    {
        var statuses = new List<CheckStatus>();
        var hasErrors = false;

        if (configService?.Settings.CodingAgents is not { Count: > 0 } agents)
            return new CheckResult(false, statuses);

        if (agentRunner == null)
            return new CheckResult(false, statuses);

        var codingAgent = configService.Settings.CodingAgent ?? "claude";

        foreach (var agent in agents)
        {
            var agentId = AgentProviderFactory.NormalizeAgentName(agent.Name);
            var isActive = agentId.Equals(codingAgent, StringComparison.OrdinalIgnoreCase);
            var label = isActive ? $"{agent.Name} (active)" : agent.Name;

            if (agent.Profiles.Count == 0)
            {
                statuses.Add(new CheckStatus(label, "No profiles configured", StatusKind.Warn));
                continue;
            }

            if (!agentRunner.RegisteredAgents.Contains(agentId, StringComparer.OrdinalIgnoreCase))
            {
                statuses.Add(new CheckStatus(label, $"Agent '{agentId}' not registered — skipping", StatusKind.Warn));
                continue;
            }

            var healthCheck = agentRunner.GetHealthCheck(agentId);
            var installStatus = await healthCheck.CheckInstallAsync();

            if (!installStatus.IsInstalled)
            {
                statuses.Add(new CheckStatus(label, $"CLI not found — skipping", StatusKind.Warn));
                continue;
            }

            statuses.Add(new CheckStatus($"  {label}", "", StatusKind.Ok));

            foreach (var profile in agent.Profiles)
            {
                if (string.IsNullOrEmpty(profile.Model))
                {
                    statuses.Add(new CheckStatus($"    {profile.Name}", "No model specified", StatusKind.Warn));
                    continue;
                }

                var result = await healthCheck.ValidateModelAsync(profile.Model);
                var profileLabel = $"    {profile.Name}: {profile.Model}";

                switch (result.Status)
                {
                    case ModelValidationStatus.Ok:
                        statuses.Add(new CheckStatus(profileLabel, "OK", StatusKind.Ok));
                        break;
                    case ModelValidationStatus.InvalidModel:
                        statuses.Add(new CheckStatus(profileLabel, result.ErrorMessage ?? "Invalid model ID", StatusKind.Error));
                        hasErrors = true;
                        break;
                    case ModelValidationStatus.AuthError:
                        statuses.Add(new CheckStatus(profileLabel, result.ErrorMessage ?? "Auth error", StatusKind.Error));
                        hasErrors = true;
                        break;
                    case ModelValidationStatus.Timeout:
                        statuses.Add(new CheckStatus(profileLabel, "Timeout", StatusKind.Warn));
                        break;
                    case ModelValidationStatus.Unknown:
                        statuses.Add(new CheckStatus(profileLabel, result.ErrorMessage ?? "Check failed", StatusKind.Warn));
                        break;
                }
            }
        }

        return new CheckResult(hasErrors, statuses);
    }
}
