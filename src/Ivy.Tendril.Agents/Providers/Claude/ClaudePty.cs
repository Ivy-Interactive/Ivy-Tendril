using System.Collections.Frozen;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Providers.Claude;

public sealed class ClaudePty : IAgentPty
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

    public TransportKind SupportedTransports => TransportKind.Pty;

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

    public IReadOnlyDictionary<string, string> GetDefaultEnvironment() =>
        new Dictionary<string, string>
        {
            ["TERM"] = "xterm-256color",
        };

    public AgentPtySpec BuildPtySpec(AgentPtyConfig config)
    {
        var args = new List<string> { "claude" };

        if (!string.IsNullOrEmpty(config.Model))
        {
            args.Add("--model");
            args.Add(config.Model);
        }

        args.Add("--permission-mode");
        args.Add(config.PermissionMode switch
        {
            PermissionMode.FullAuto => "dontAsk",
            PermissionMode.AcceptEdits => "acceptEdits",
            PermissionMode.Plan => "plan",
            _ => "default"
        });

        if (!string.IsNullOrEmpty(config.SessionId))
        {
            args.Add("--session-id");
            args.Add(config.SessionId);
        }

        if (!string.IsNullOrEmpty(config.SystemPrompt))
        {
            args.Add(config.AppendSystemPrompt ? "--append-system-prompt" : "--system-prompt");
            args.Add(config.SystemPrompt);
        }

        foreach (var mcp in config.McpServers)
        {
            args.Add("--mcp-server");
            args.Add(mcp.Name);
        }

        foreach (var arg in config.ExtraArguments)
            args.Add(arg);

        var env = new Dictionary<string, string>(GetDefaultEnvironment());
        foreach (var (key, value) in config.EnvironmentVariables)
            env[key] = value;

        return new AgentPtySpec
        {
            CommandLine = args,
            WorkingDirectory = config.WorkingDirectory,
            Environment = env,
        };
    }

    public AgentActivityPatterns? GetActivityPatterns() => new()
    {
        WorkingPattern = @"⠋|⠙|⠹|⠸|⠼|⠴|⠦|⠧|⠇|⠏|●",
        IdlePattern = @"❯|>\s*$",
        ErrorPattern = @"Error:|error:|ERR!",
        PermissionPromptPattern = @"Allow|Deny|approve|reject",
    };
}
