using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Providers.Codex;

public sealed class CodexCli : IAgentCli
{
    public string Id => AgentId.Codex;
    public string DisplayName => "Codex";

    public AgentCapabilities Capabilities =>
        AgentCapabilities.StdinPrompt |
        AgentCapabilities.StreamJsonOutput |
        AgentCapabilities.ModelSelection |
        AgentCapabilities.EffortControl |
        AgentCapabilities.DirectoryRestriction |
        AgentCapabilities.HealthCheck |
        AgentCapabilities.ExtraArgPassthrough;

    public TransportKind SupportedTransports => TransportKind.CliSpawn;
    public PromptTransport PromptTransport => PromptTransport.Stdin;
    public OutputFormat PreferredOutputFormat => OutputFormat.StreamJson;

    public IReadOnlyList<AgentProfileDefault> DefaultProfiles { get; } =
    [
        new(ProfileTier.Deep, "gpt-5.6-sol", "high"),
        new(ProfileTier.Balanced, "gpt-5.6-terra", "medium"),
        new(ProfileTier.Quick, "gpt-5.6-luna", "low"),
    ];

    public IReadOnlyList<EffortOption> SupportedEfforts => EffortLevels.Codex;

    private static readonly FrozenDictionary<string, string> ToolNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [CanonicalTools.Bash] = "bash",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string> ReverseToolNameMap =
        ToolNameMap.ToDictionary(kvp => kvp.Value, kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly Regex WritableDirRegex = new(
        @"^(?:Write|Edit)\((.+?)(?:[/\\]\*\*?)?\)$",
        RegexOptions.Compiled);

    public string? TranslateToolName(string canonicalTool)
        => ToolNameMap.GetValueOrDefault(canonicalTool);

    public string? ReverseTranslateToolName(string nativeTool)
        => ReverseToolNameMap.GetValueOrDefault(nativeTool);

    public IReadOnlyList<string> ExtractWritableDirectories(IReadOnlyList<string> allowedTools)
    {
        var dirs = new List<string>();
        foreach (var tool in allowedTools)
        {
            var match = WritableDirRegex.Match(tool);
            if (match.Success)
                dirs.Add(match.Groups[1].Value);
        }
        return dirs;
    }

    public AgentProcessSpec BuildProcessSpec(AgentLaunchConfig config)
    {
        var args = new List<string> { "exec" };

        switch (config.PermissionMode)
        {
            case PermissionMode.FullAuto:
                args.Add("--sandbox");
                args.Add("danger-full-access");
                args.Add("--ask-for-approval");
                args.Add("never");
                break;

            case PermissionMode.AcceptEdits:
                args.Add("--sandbox");
                args.Add("workspace-write");
                args.Add("-c");
                args.Add("sandbox_workspace_write.network_access=true");
                args.Add("-c");
                args.Add("sandbox_permissions=[\"disk-full-read-access\"]");
                args.Add("--ask-for-approval");
                args.Add("never");
                break;

            case PermissionMode.Plan:
                args.Add("--sandbox");
                args.Add("read-only");
                break;

            case PermissionMode.Default:
            default:
                args.Add("--sandbox");
                args.Add("workspace-write");
                args.Add("-c");
                args.Add("sandbox_workspace_write.network_access=true");
                break;
        }

        args.Add("--json");
        args.Add("--skip-git-repo-check");

        if (!string.IsNullOrEmpty(config.Model))
        {
            args.Add("--model");
            args.Add(config.Model);
        }

        if (config.Effort is not null)
        {
            var effort = config.Effort.Value switch
            {
                EffortLevel.Low => "low",
                EffortLevel.Medium => "medium",
                EffortLevel.High => "high",
                EffortLevel.XHigh => "xhigh",
                EffortLevel.Max => "xhigh",
                _ => "medium"
            };
            args.Add("-c");
            args.Add($"model_reasoning_effort=\"{effort}\"");
        }

        var extractedDirs = ExtractWritableDirectories(config.AllowedTools);
        var allDirs = new HashSet<string>(extractedDirs, StringComparer.OrdinalIgnoreCase);
        foreach (var dir in config.WritableDirectories)
            allDirs.Add(dir);

        foreach (var dir in allDirs)
        {
            args.Add("--add-dir");
            args.Add(dir);
        }

        var tempFiles = new List<string>();

        var mcpConfigFile = global::Ivy.Tendril.Agents.Helpers.McpConfigWriter.WriteConfigFile(config.McpServers);
        if (!string.IsNullOrEmpty(mcpConfigFile))
        {
            tempFiles.Add(mcpConfigFile);
            args.Add("--mcp-config");
            args.Add(mcpConfigFile);
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
            FileName = "codex",
            Arguments = args,
            WorkingDirectory = config.WorkingDirectory,
            Environment = env,
            StdinContent = config.Prompt,
            RedirectStdin = true,
            TempFiles = tempFiles,
        };
    }

    public IReadOnlyDictionary<string, string> GetDefaultEnvironment() =>
        new Dictionary<string, string>
        {
            ["CI"] = "true",
            ["TERM"] = "dumb"
        };
}
