using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Services;

public record AgentResolution(
    IAgentCli Cli,
    string Model,
    string Effort,
    // Name of the profile whose settings actually applied — after the promptware defaults, the
    // caller's override and the balanced/default fallback have all had their say. Empty when no
    // profile matched, so nothing shaped the model or effort.
    string Profile,
    IReadOnlyList<string> AllowedTools,
    IReadOnlyList<string> ExtraArgs,
    IReadOnlyDictionary<string, string> EnvironmentVariables)
{
    public string AgentId => Cli.Id;
    public bool UsesStdinPrompt => Cli.PromptTransport == PromptTransport.Stdin;
}

public static class AgentProviderFactory
{
    internal static readonly IReadOnlyList<string> BaseTools =
        ["Read", "Glob", "Grep", "Bash", "WebFetch", "WebSearch"];

    private static readonly Dictionary<string, IReadOnlyList<string>> BuiltInExtraTools =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["ExecutePlan"] = ["Write", "Edit"],
            ["RetryPlan"] = ["Write", "Edit"],
            ["IvyFrameworkVerification"] = ["Write", "Edit"],
        };

    public static AgentResolution Resolve(
        IAgentRunner runner,
        TendrilSettings settings,
        string promptwareName,
        string? profileOverride = null,
        IReadOnlyDictionary<string, string>? jobContext = null,
        string? agentOverride = null)
    {
        var codingAgent = agentOverride ?? settings.CodingAgent;
        if (string.IsNullOrEmpty(codingAgent))
            throw new InvalidOperationException("No coding agent configured. Set 'codingAgent' in config.yaml.");
        var cli = runner.GetCli(codingAgent);

        var allowedTools = ResolveAllowedTools(settings, promptwareName, jobContext);
        var (profileName, extraArgs, envVars) = ResolveAgentConfig(settings, codingAgent, promptwareName, profileOverride);
        var (model, effort, appliedProfile) = ApplyProfile(settings, codingAgent, profileName, cli, extraArgs);

        return new AgentResolution(cli, model, effort, appliedProfile, allowedTools, extraArgs, envVars);
    }

    private static List<string> ResolveAllowedTools(
        TendrilSettings settings,
        string promptwareName,
        IReadOnlyDictionary<string, string>? jobContext)
    {
        var allowedTools = new List<string>(BaseTools);

        if (BuiltInExtraTools.TryGetValue(promptwareName, out var builtInExtras))
            allowedTools.AddRange(builtInExtras);

        if (settings.Promptwares.TryGetValue("_default", out var defaultConfig) && defaultConfig.AllowedTools.Count > 0)
            allowedTools.AddRange(defaultConfig.AllowedTools);

        if (!string.IsNullOrEmpty(promptwareName) &&
            settings.Promptwares.TryGetValue(promptwareName, out var specificConfig) &&
            specificConfig.AllowedTools.Count > 0)
            allowedTools.AddRange(specificConfig.AllowedTools);

        for (var i = 0; i < allowedTools.Count; i++)
        {
            var tool = allowedTools[i];
            if (jobContext != null)
            {
                foreach (var (key, value) in jobContext)
                    tool = tool.Replace($"%{key}%", value, StringComparison.OrdinalIgnoreCase);
            }
            allowedTools[i] = Environment.ExpandEnvironmentVariables(tool).Replace('\\', '/');
        }

        return allowedTools.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static (string ProfileName, List<string> ExtraArgs, IReadOnlyDictionary<string, string> EnvironmentVariables) ResolveAgentConfig(
        TendrilSettings settings,
        string codingAgent,
        string promptwareName,
        string? profileOverride)
    {
        var profileName = "";
        var extraArgs = new List<string>();

        if (settings.Promptwares.TryGetValue("_default", out var defaultConfig) &&
            !string.IsNullOrEmpty(defaultConfig.Profile))
            profileName = defaultConfig.Profile;

        if (!string.IsNullOrEmpty(promptwareName) &&
            settings.Promptwares.TryGetValue(promptwareName, out var specificConfig) &&
            !string.IsNullOrEmpty(specificConfig.Profile))
            profileName = specificConfig.Profile;

        if (!string.IsNullOrEmpty(profileOverride))
            profileName = profileOverride;

        var agentConfig = settings.CodingAgents.FirstOrDefault(a =>
            NormalizeAgentName(a.Name).Equals(codingAgent, StringComparison.OrdinalIgnoreCase));

        if (agentConfig != null && !string.IsNullOrWhiteSpace(agentConfig.Arguments))
            extraArgs.AddRange(SplitArgs(agentConfig.Arguments));

        IReadOnlyDictionary<string, string> envVars = agentConfig?.EnvironmentVariables is { Count: > 0 }
            ? agentConfig.EnvironmentVariables
            : new Dictionary<string, string>();

        return (profileName, extraArgs, envVars);
    }

    private static (string Model, string Effort, string AppliedProfile) ApplyProfile(
        TendrilSettings settings,
        string codingAgent,
        string profileName,
        IAgentCli cli,
        List<string> extraArgs)
    {
        var agentConfig = settings.CodingAgents.FirstOrDefault(a =>
            NormalizeAgentName(a.Name).Equals(codingAgent, StringComparison.OrdinalIgnoreCase));

        if (agentConfig == null && string.IsNullOrEmpty(profileName))
            return ("", "", "");

        AgentProfileConfig? profile = null;
        if (!string.IsNullOrEmpty(profileName) && agentConfig != null)
        {
            profile = agentConfig.Profiles.FirstOrDefault(p =>
                p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));
        }

        if (profile == null && agentConfig != null && agentConfig.Profiles.Count > 0)
        {
            profile = agentConfig.Profiles.FirstOrDefault(p => p.Name.Equals("balanced", StringComparison.OrdinalIgnoreCase))
                ?? agentConfig.Profiles.FirstOrDefault(p => p.Name.Equals("default", StringComparison.OrdinalIgnoreCase))
                ?? agentConfig.Profiles.FirstOrDefault(p => !string.IsNullOrEmpty(p.Model) && !p.Model.Equals("default", StringComparison.OrdinalIgnoreCase));
        }

        var model = "";
        var effort = "";

        if (profile != null)
        {
            if (!string.IsNullOrEmpty(profile.Model) &&
                !profile.Model.Equals("default", StringComparison.OrdinalIgnoreCase) &&
                cli.Capabilities.HasFlag(AgentCapabilities.ModelSelection))
                model = profile.Model;
            if (!string.IsNullOrEmpty(profile.Effort) &&
                cli.Capabilities.HasFlag(AgentCapabilities.EffortControl))
                effort = profile.Effort;
            if (!string.IsNullOrWhiteSpace(profile.Arguments))
                extraArgs.AddRange(SplitArgs(profile.Arguments));
        }

        // The applied profile is the one that was found, not the one that was asked for: an override
        // naming a profile the agent does not define falls through to balanced/default below, and
        // recording the request rather than the fallback would misreport what the job ran under.
        return (model, effort, profile?.Name ?? "");
    }

    internal static string NormalizeAgentName(string name) => name.ToLowerInvariant() switch
    {
        "claudecode" => "claude",
        _ => name.ToLowerInvariant()
    };

    internal static EffortLevel? ParseEffort(string effort) => effort.ToLowerInvariant() switch
    {
        "low" => EffortLevel.Low,
        "medium" => EffortLevel.Medium,
        "high" => EffortLevel.High,
        "xhigh" => EffortLevel.XHigh,
        "max" => EffortLevel.Max,
        _ => string.IsNullOrEmpty(effort) ? null : EffortLevel.Medium
    };

    private static IEnumerable<string> SplitArgs(string args) =>
        args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
