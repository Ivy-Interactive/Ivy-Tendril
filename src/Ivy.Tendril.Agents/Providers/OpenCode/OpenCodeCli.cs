using System.Collections.Frozen;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Providers.OpenCode;

public sealed class OpenCodeCli : IAgentCli
{
    public string Id => AgentId.OpenCode;
    public string DisplayName => "OpenCode";

    public AgentCapabilities Capabilities =>
        AgentCapabilities.StdinPrompt |
        AgentCapabilities.StreamJsonOutput |
        AgentCapabilities.CostInOutput |
        AgentCapabilities.ModelSelection |
        AgentCapabilities.EffortControl |
        AgentCapabilities.SessionResume |
        AgentCapabilities.HealthCheck |
        AgentCapabilities.ExtraArgPassthrough;

    public TransportKind SupportedTransports => TransportKind.CliSpawn;
    public PromptTransport PromptTransport => PromptTransport.Stdin;
    public OutputFormat PreferredOutputFormat => OutputFormat.StreamJson;

    private static readonly FrozenDictionary<string, string> ToolMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [CanonicalTools.Read] = "read",
        [CanonicalTools.Write] = "write",
        [CanonicalTools.Edit] = "edit",
        [CanonicalTools.Bash] = "bash",
        [CanonicalTools.Glob] = "glob",
        [CanonicalTools.Grep] = "search",
        [CanonicalTools.WebFetch] = "web_fetch",
        [CanonicalTools.WebSearch] = "web_search",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string> ReverseToolMap =
        ToolMap.ToDictionary(kvp => kvp.Value, kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public string? TranslateToolName(string canonicalTool)
        => ToolMap.GetValueOrDefault(canonicalTool);

    public string? ReverseTranslateToolName(string nativeTool)
        => ReverseToolMap.GetValueOrDefault(nativeTool);

    public IReadOnlyList<string> ExtractWritableDirectories(IReadOnlyList<string> allowedTools) => [];

    public AgentProcessSpec BuildProcessSpec(AgentLaunchConfig config)
    {
        var args = new List<string>
        {
            "run",
            "--dangerously-skip-permissions",
            "--format", "json"
        };

        if (!string.IsNullOrEmpty(config.Model))
        {
            args.Add("--model");
            args.Add(config.Model);
        }

        if (config.Effort is not null)
        {
            args.Add("--variant");
            args.Add(config.Effort.Value switch
            {
                EffortLevel.Low => "low",
                EffortLevel.Medium => "medium",
                EffortLevel.High => "high",
                EffortLevel.XHigh => "max",
                EffortLevel.Max => "max",
                _ => "medium"
            });
        }

        // OpenCode's --session resumes an existing session; it does not accept
        // caller-assigned IDs for new sessions (unlike Claude's --session-id).

        foreach (var arg in config.ExtraArguments)
            args.Add(arg);

        var env = new Dictionary<string, string>(GetDefaultEnvironment());
        if (config.EnvironmentVariables is not null)
        {
            foreach (var (key, value) in config.EnvironmentVariables)
                env[key] = value;
        }

        return new AgentProcessSpec
        {
            FileName = "opencode",
            Arguments = args,
            WorkingDirectory = config.WorkingDirectory,
            Environment = env,
            StdinContent = config.Prompt,
            RedirectStdin = true,
        };
    }

    public IReadOnlyDictionary<string, string> GetDefaultEnvironment() =>
        new Dictionary<string, string>
        {
            ["CI"] = "true",
            ["TERM"] = "dumb"
        };
}
