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

        if (configService?.Settings == null || agentRunner == null)
            return new CheckResult(false, statuses);

        var codingAgent = configService.Settings.CodingAgent ?? "claude";
        var configuredAgents = configService.Settings.CodingAgents ?? [];

        foreach (var agentId in agentRunner.RegisteredAgents)
        {
            var isActive = agentId.Equals(codingAgent, StringComparison.OrdinalIgnoreCase);
            var label = isActive ? $"{agentId} (active)" : agentId;

            var healthCheck = agentRunner.GetHealthCheck(agentId);
            var installStatus = await healthCheck.CheckInstallAsync();

            if (!installStatus.IsInstalled)
            {
                statuses.Add(new CheckStatus(label, "CLI not found — skipping", StatusKind.Warn));
                continue;
            }

            statuses.Add(new CheckStatus(label, "", StatusKind.Ok));

            // Use explicit config profiles if available, otherwise use default profiles
            var agentConfig = configuredAgents.FirstOrDefault(a =>
                AgentProviderFactory.NormalizeAgentName(a.Name).Equals(agentId, StringComparison.OrdinalIgnoreCase));

            var validatedModels = new Dictionary<string, ModelValidationResult>(StringComparer.OrdinalIgnoreCase);

            if (agentConfig is { Profiles.Count: > 0 })
            {
                foreach (var profile in agentConfig.Profiles)
                {
                    var model = string.IsNullOrEmpty(profile.Model) ? "default" : profile.Model;
                    if (!validatedModels.TryGetValue(model, out var result))
                    {
                        result = await healthCheck.ValidateModelAsync(model);
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
                        result = await healthCheck.ValidateModelAsync(model);
                        validatedModels[model] = result;
                    }

                    var profileLabel = $"  {profile.Tier.ToString().ToLowerInvariant()}: {model}";
                    AddProfileStatus(statuses, profileLabel, result, ref hasErrors);
                }
            }
        }

        return new CheckResult(hasErrors, statuses);
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
