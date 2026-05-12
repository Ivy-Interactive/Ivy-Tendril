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
        ["gemini"] = new GeminiAgentProvider(),
        ["copilot"] = new CopilotAgentProvider(),
        ["opencode"] = new OpenCodeAgentProvider()
    };

    internal static readonly IReadOnlyList<string> BaseTools =
        ["Read", "Glob", "Grep", "Bash(tendril*)", "Bash(git *)", "Bash(gh *)", "Bash(ls *)", "Bash(find *)", "Bash(cat *)",
         "WebFetch", "WebSearch", "Write(%PROMPTWARE_DIR%/**)", "Edit(%PROMPTWARE_DIR%/**)", "Bash(%PROMPTWARE_DIR%/Tools/*)",
         "Write(%TENDRIL_HOME%/Temp/**)"];

    private static readonly Dictionary<string, IReadOnlyList<string>> BuiltInExtraTools =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["ExecutePlan"] = ["Bash", "Write(%PLAN_DIR%/**)", "Edit(%PLAN_DIR%/**)"],
            ["CreatePlan"] = ["Write(%TENDRIL_HOME%/Trash/**)"],
            ["CreatePr"] = ["Bash"],
            ["CreateIssue"] = ["Bash"]
        };

    public static IAgentProvider GetProvider(string name)
    {
        if (Providers.TryGetValue(name, out var provider))
            return provider;

        throw new ArgumentException($"Unknown agent provider: {name}. Available: {string.Join(", ", Providers.Keys)}");
    }

    public static AgentResolution Resolve(
        TendrilSettings settings,
        string promptwareName,
        string? profileOverride = null,
        IReadOnlyDictionary<string, string>? jobContext = null,
        string? agentOverride = null)
    {
        var codingAgent = agentOverride ?? settings.CodingAgent;
        var provider = GetProvider(codingAgent);

        // Built-in defaults: BaseTools + per-promptware extras
        string profileName = "";
        var allowedTools = new List<string>(BaseTools);

        if (BuiltInExtraTools.TryGetValue(promptwareName, out var builtInExtras))
            allowedTools.AddRange(builtInExtras);

        // Layer config on top: _default → specific promptware → profileOverride
        if (settings.Promptwares.TryGetValue("_default", out var defaultConfig))
        {
            if (!string.IsNullOrEmpty(defaultConfig.Profile))
                profileName = defaultConfig.Profile;
            if (defaultConfig.AllowedTools.Count > 0)
                allowedTools.AddRange(defaultConfig.AllowedTools);
        }

        if (!string.IsNullOrEmpty(promptwareName) &&
            settings.Promptwares.TryGetValue(promptwareName, out var specificConfig))
        {
            if (!string.IsNullOrEmpty(specificConfig.Profile))
                profileName = specificConfig.Profile;
            if (specificConfig.AllowedTools.Count > 0)
                allowedTools.AddRange(specificConfig.AllowedTools);
        }

        if (!string.IsNullOrEmpty(profileOverride))
            profileName = profileOverride;

        // Expand job context variables (%PLAN_DIR%, %PLANS_DIR%, etc.) then env vars
        for (var i = 0; i < allowedTools.Count; i++)
        {
            var tool = allowedTools[i];
            if (jobContext != null)
            {
                foreach (var (key, value) in jobContext)
                    tool = tool.Replace($"%{key}%", value, StringComparison.OrdinalIgnoreCase);
            }
            allowedTools[i] = Environment.ExpandEnvironmentVariables(tool)
                .Replace('\\', '/');
        }

        // Deduplicate (config may re-state base tools)
        allowedTools = allowedTools.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // Resolve model, effort, and extra args from agent config + profile
        string model = "";
        string effort = "";
        var extraArgs = new List<string>();

        var agentConfig = settings.CodingAgents.FirstOrDefault(a =>
            a.Name.Equals(codingAgent, StringComparison.OrdinalIgnoreCase) ||
            (a.Name.Equals("ClaudeCode", StringComparison.OrdinalIgnoreCase) && codingAgent == "claude") ||
            (a.Name.Equals("Codex", StringComparison.OrdinalIgnoreCase) && codingAgent == "codex") ||
            (a.Name.Equals("Gemini", StringComparison.OrdinalIgnoreCase) && codingAgent == "gemini") ||
            (a.Name.Equals("Copilot", StringComparison.OrdinalIgnoreCase) && codingAgent == "copilot") ||
            (a.Name.Equals("OpenCode", StringComparison.OrdinalIgnoreCase) && codingAgent == "opencode"));

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
