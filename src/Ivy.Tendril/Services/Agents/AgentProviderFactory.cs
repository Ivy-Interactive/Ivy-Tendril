namespace Ivy.Tendril.Services.Agents;

public record AgentResolution(
    IAgentProvider Provider,
    string Model,
    string Effort,
    IReadOnlyList<string> AllowedTools,
    IReadOnlyList<string> ExtraArgs);

public class AgentProviderFactory
{
    private static readonly Dictionary<string, IAgentProvider> Providers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude"] = new ClaudeAgentProvider(),
        ["codex"] = new CodexAgentProvider(),
        ["gemini"] = new GeminiAgentProvider()
    };

    public static IAgentProvider GetProvider(string name)
    {
        if (Providers.TryGetValue(name, out var provider))
            return provider;

        throw new ArgumentException($"Unknown agent provider: {name}. Available: {string.Join(", ", Providers.Keys)}");
    }

    public static AgentResolution Resolve(TendrilSettings settings, string promptwareName, string? profileOverride = null)
    {
        var codingAgent = settings.CodingAgent;
        var provider = GetProvider(codingAgent);

        // Layer promptware config: _default → specific → profileOverride
        string profileName = "";
        var allowedTools = new List<string>();

        if (settings.Promptwares.TryGetValue("_default", out var defaultConfig))
        {
            if (!string.IsNullOrEmpty(defaultConfig.Profile))
                profileName = defaultConfig.Profile;
            allowedTools.AddRange(defaultConfig.AllowedTools);
        }

        if (!string.IsNullOrEmpty(promptwareName) &&
            settings.Promptwares.TryGetValue(promptwareName, out var specificConfig))
        {
            if (!string.IsNullOrEmpty(specificConfig.Profile))
                profileName = specificConfig.Profile;
            if (specificConfig.AllowedTools.Count > 0)
                allowedTools = new List<string>(specificConfig.AllowedTools);
        }

        if (!string.IsNullOrEmpty(profileOverride))
            profileName = profileOverride;

        // Expand environment variables in allowed tools
        for (var i = 0; i < allowedTools.Count; i++)
        {
            allowedTools[i] = Environment.ExpandEnvironmentVariables(allowedTools[i])
                .Replace('\\', '/');
        }

        // Resolve model, effort, and extra args from agent config + profile
        string model = "";
        string effort = "";
        var extraArgs = new List<string>();

        var agentConfig = settings.CodingAgents.FirstOrDefault(a =>
            a.Name.Equals(codingAgent, StringComparison.OrdinalIgnoreCase) ||
            (a.Name.Equals("ClaudeCode", StringComparison.OrdinalIgnoreCase) && codingAgent == "claude") ||
            (a.Name.Equals("Codex", StringComparison.OrdinalIgnoreCase) && codingAgent == "codex") ||
            (a.Name.Equals("Gemini", StringComparison.OrdinalIgnoreCase) && codingAgent == "gemini"));

        if (agentConfig != null)
        {
            if (!string.IsNullOrWhiteSpace(agentConfig.Arguments))
                extraArgs.AddRange(SplitArgs(agentConfig.Arguments));

            if (!string.IsNullOrEmpty(profileName))
            {
                var profile = agentConfig.Profiles.FirstOrDefault(p =>
                    p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));

                if (profile != null)
                {
                    if (!string.IsNullOrEmpty(profile.Model)) model = profile.Model;
                    if (!string.IsNullOrEmpty(profile.Effort)) effort = profile.Effort;
                    if (!string.IsNullOrWhiteSpace(profile.Arguments))
                        extraArgs.AddRange(SplitArgs(profile.Arguments));
                }
            }
        }

        return new AgentResolution(provider, model, effort, allowedTools, extraArgs);
    }

    private static IEnumerable<string> SplitArgs(string args) =>
        args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
