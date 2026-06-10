using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Services;

public record AgentResolution(
    IAgentCli Cli,
    string Model,
    string Effort,
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
        var (model, effort) = ApplyProfile(settings, codingAgent, profileName, cli, extraArgs);

        return new AgentResolution(cli, model, effort, allowedTools, extraArgs, envVars);
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

    private static (string Model, string Effort) ApplyProfile(
        TendrilSettings settings,
        string codingAgent,
        string profileName,
        IAgentCli cli,
        List<string> extraArgs)
    {
        if (string.IsNullOrEmpty(profileName))
            return ("", "");

        var agentConfig = settings.CodingAgents.FirstOrDefault(a =>
            NormalizeAgentName(a.Name).Equals(codingAgent, StringComparison.OrdinalIgnoreCase));

        if (agentConfig == null)
            return ("", "");

        var profile = agentConfig.Profiles.FirstOrDefault(p =>
            p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));

        if (profile == null)
            return ("", "");

        var model = "";
        var effort = "";

        if (!string.IsNullOrEmpty(profile.Model) &&
            !profile.Model.Equals("default", StringComparison.OrdinalIgnoreCase) &&
            cli.Capabilities.HasFlag(AgentCapabilities.ModelSelection))
            model = profile.Model;
        if (!string.IsNullOrEmpty(profile.Effort) &&
            cli.Capabilities.HasFlag(AgentCapabilities.EffortControl))
            effort = profile.Effort;
        if (!string.IsNullOrWhiteSpace(profile.Arguments))
            extraArgs.AddRange(SplitArgs(profile.Arguments));

        return (model, effort);
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
