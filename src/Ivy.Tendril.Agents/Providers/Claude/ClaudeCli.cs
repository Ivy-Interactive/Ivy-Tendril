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

    public IReadOnlyList<EffortOption> SupportedEfforts { get; } =
    [
        new("low", "Low"),
        new("medium", "Medium"),
        new("high", "High"),
        new("xhigh", "Extra High"),
        new("max", "Max"),
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

        var allowedRules = new List<string>();

        if (config.AllowedTools.Count > 0)
        {
            var activeTools = new List<string>();
            foreach (var tool in config.AllowedTools)
            {
                if (tool.StartsWith("Bash(", StringComparison.OrdinalIgnoreCase) && tool.EndsWith(")"))
                {
                    allowedRules.Add(tool);
                    if (!activeTools.Contains("Bash")) activeTools.Add("Bash");
                }
                else
                {
                    var nativeName = TranslateToolName(tool) ?? tool;
                    if (nativeName.Equals("Bash", StringComparison.OrdinalIgnoreCase))
                    {
                        allowedRules.Add("Bash(*)");
                    }
                    else
                    {
                        allowedRules.Add(nativeName);
                    }
                    if (!activeTools.Contains(nativeName)) activeTools.Add(nativeName);
                }
            }

            if (activeTools.Count > 0)
            {
                args.Add("--tools");
                args.Add(string.Join(",", activeTools));
            }
        }
        else if (config.PermissionMode == PermissionMode.FullAuto)
        {
            // Tailor default safe/essential permissions needed to work with Tendril
            allowedRules.AddRange([
                "Read", "Write", "Edit", "Glob", "Grep", "WebFetch", "WebSearch",
                "Bash(tendril *)",
                "Bash(git *)",
                "Bash(gh *)",
                "Bash(dotnet *)",
                "Bash(pnpm *)",
                "Bash(npm *)",
                "Bash(yarn *)",
                "Bash(bun *)",
                "Bash(node *)",
                "Bash(ls *)",
                "Bash(find *)",
                "Bash(cat *)",
                "Bash(head *)",
                "Bash(tail *)",
                "Bash(grep *)",
                "Bash(mkdir *)",
                "Bash(rmdir *)",
                "Bash(rm *)",
                "Bash(cp *)",
                "Bash(mv *)",
                "Bash(touch *)",
                "Bash(chmod *)",
                "Bash(pwd)",
                "Bash(echo *)",
                "Bash(which *)",
                "Bash(npx *)",
                "Bash(vite *)",
                "Bash(tsc *)",
                "Bash(eslint *)",
                "Bash(prettier *)",
                "Bash(python *)",
                "Bash(python3 *)",
                "Bash(pip *)",
                "Bash(pip3 *)",
                "Bash(uv *)",
                "Bash(refitter *)",
                "Bash(svcutil *)",
                "Bash(ivy-inspector-*)",
                "Bash(dotnet-*)",
                "Bash(strawberryshake *)"
            ]);
        }

        if (allowedRules.Count > 0)
        {
            var settingsObj = new
            {
                permissions = new
                {
                    allow = allowedRules
                }
            };
            var settingsJson = System.Text.Json.JsonSerializer.Serialize(settingsObj);
            args.Add("--settings");
            args.Add(settingsJson);
        }

        if (!string.IsNullOrEmpty(config.Model))
        {
            args.Add("--model");
            args.Add(NormalizeClaudeModel(config.Model));
        }

        if (config.Effort is not null)
        {
            args.Add("--effort");
            args.Add(config.Effort.Value switch
            {
                EffortLevel.Low => "low",
                EffortLevel.Medium => "medium",
                EffortLevel.High => "high",
                EffortLevel.XHigh => "xhigh",
                EffortLevel.Max => "max",
                _ => "medium"
            });
        }

        if (!string.IsNullOrEmpty(config.SessionId))
        {
            args.Add("--session-id");
            args.Add(config.SessionId);
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

        var mcpConfigFile = global::Ivy.Tendril.Agents.Helpers.McpConfigWriter.WriteConfigFile(config.McpServers);
        if (!string.IsNullOrEmpty(mcpConfigFile))
        {
            args.Add("--mcp-config");
            args.Add(mcpConfigFile);
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

    private static string NormalizeClaudeModel(string model)
    {
        if (string.Equals(model, "default", StringComparison.OrdinalIgnoreCase)) return "opus";
        if (model.Contains("opus", StringComparison.OrdinalIgnoreCase)) return "opus";
        if (model.Contains("sonnet", StringComparison.OrdinalIgnoreCase)) return "sonnet";
        if (model.Contains("haiku", StringComparison.OrdinalIgnoreCase)) return "haiku";
        return model;
    }
}
