using System.Collections.Frozen;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Providers.Claude;

public sealed class ClaudeCli : IAgentCli
{
    public string Id => Abstractions.AgentId.Claude;
    public string DisplayName => "Claude Code";

    public AgentCapabilities Capabilities =>
        AgentCapabilities.StdinPrompt |
        AgentCapabilities.StreamJsonOutput |
        AgentCapabilities.CostInOutput |
        AgentCapabilities.ModelSelection |
        AgentCapabilities.EffortControl |
        AgentCapabilities.ToolAllowlisting |
        AgentCapabilities.SessionResume |
        AgentCapabilities.PermissionDenialReporting |
        AgentCapabilities.HealthCheck |
        AgentCapabilities.ExtraArgPassthrough |
        AgentCapabilities.MaxTurns;

    public TransportKind SupportedTransports => TransportKind.CliSpawn;
    public PromptTransport PromptTransport => PromptTransport.Stdin;
    public OutputFormat PreferredOutputFormat => OutputFormat.StreamJson;

    public IReadOnlyList<AgentProfileDefault> DefaultProfiles { get; } =
    [
        new(ProfileTier.Deep, "opus", "max"),
        new(ProfileTier.Balanced, "sonnet", "high"),
        new(ProfileTier.Quick, "haiku", "low"),
    ];

    private static readonly FrozenDictionary<string, string> ToolNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [CanonicalTools.Read] = "Read",
        [CanonicalTools.Write] = "Write",
        [CanonicalTools.Edit] = "Edit",
        [CanonicalTools.Bash] = "Bash",
        [CanonicalTools.Glob] = "Glob",
        [CanonicalTools.Grep] = "Grep",
        [CanonicalTools.WebFetch] = "WebFetch",
        [CanonicalTools.WebSearch] = "WebSearch",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string> ReverseToolNameMap =
        ToolNameMap.ToDictionary(kvp => kvp.Value, kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public string? TranslateToolName(string canonicalTool)
        => ToolNameMap.GetValueOrDefault(canonicalTool);

    public string? ReverseTranslateToolName(string nativeTool)
        => ReverseToolNameMap.GetValueOrDefault(nativeTool);

    public IReadOnlyList<string> ExtractWritableDirectories(IReadOnlyList<string> allowedTools) => [];

    public AgentProcessSpec BuildProcessSpec(AgentLaunchConfig config)
    {
        var args = new List<string>
        {
            "--print",
            "--verbose",
            "--output-format", "stream-json",
            "--permission-mode",
            config.PermissionMode switch
            {
                PermissionMode.FullAuto => "dontAsk",
                PermissionMode.AcceptEdits => "acceptEdits",
                PermissionMode.Plan => "plan",
                _ => "default"
            }
        };

        if (!string.IsNullOrEmpty(config.Model))
        {
            args.Add("--model");
            args.Add(config.Model);
        }

        if (config.Effort is not null)
        {
            args.Add("--effort");
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

        if (!string.IsNullOrEmpty(config.SessionId))
        {
            args.Add("--session-id");
            args.Add(config.SessionId);
        }

        if (config.AllowedTools.Count > 0)
        {
            args.Add("--allowedTools");
            args.Add(string.Join(" ", config.AllowedTools));
        }

        if (config.MaxTurns.HasValue)
        {
            args.Add("--max-turns");
            args.Add(config.MaxTurns.Value.ToString());
        }

        if (!string.IsNullOrEmpty(config.SystemPrompt))
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"tendril-sysprompt-{Guid.NewGuid():N}.md");
            File.WriteAllText(tempFile, config.SystemPrompt);
            args.Add("--system-prompt-file");
            args.Add(tempFile);
        }

        foreach (var mcp in config.McpServers)
        {
            args.Add("--mcp-server");
            args.Add(mcp.Name);
        }

        foreach (var arg in config.ExtraArguments)
            args.Add(arg);

        args.Add("-");

        var env = new Dictionary<string, string>(GetDefaultEnvironment());
        if (config.EnvironmentVariables is not null)
        {
            foreach (var (key, value) in config.EnvironmentVariables)
                env[key] = value;
        }

        return new AgentProcessSpec
        {
            FileName = "claude",
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
