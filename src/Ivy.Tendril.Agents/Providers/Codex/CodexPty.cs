using System.Collections.Frozen;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Providers.Codex;

public sealed class CodexPty : IAgentPty
{
    public string Id => AgentId.Codex;
    public string DisplayName => "Codex";

    public AgentCapabilities Capabilities =>
        AgentCapabilities.StdinPrompt |
        AgentCapabilities.StreamJsonOutput |
        AgentCapabilities.ModelSelection |
        AgentCapabilities.DirectoryRestriction |
        AgentCapabilities.HealthCheck |
        AgentCapabilities.ExtraArgPassthrough;

    public TransportKind SupportedTransports => TransportKind.Pty;
    public IReadOnlyList<AgentProfileDefault> DefaultProfiles => [];

    public string? ContextFileName => "AGENTS.md";

    private static readonly FrozenDictionary<string, string> ToolNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [CanonicalTools.Bash] = "bash",
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
        var args = new List<string> { "codex" };

        // FullAuto → run without approval prompts (workspace-write sandbox).
        // The interactive TUI has no `--full-auto` shorthand (that lived on the
        // legacy top-level CLI); compose it from the sandbox + approval flags.
        if (config.PermissionMode == PermissionMode.FullAuto)
        {
            args.Add("--sandbox");
            args.Add("workspace-write");
            args.Add("--ask-for-approval");
            args.Add("never");
        }

        if (!string.IsNullOrEmpty(config.Model))
        {
            args.Add("--model");
            args.Add(config.Model);
        }

        foreach (var arg in config.ExtraArguments)
            args.Add(arg);

        // Initial task as the trailing positional [PROMPT] — must come after all options.
        if (!string.IsNullOrEmpty(config.InitialPrompt))
            args.Add(config.InitialPrompt);

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
        IdlePattern = @">\s*$",
        ErrorPattern = @"Error:|error:|ERR!",
        // First-run "Do you trust the contents of this directory?" prompt; default "Yes" → Enter.
        TrustPromptPattern = @"Do you trust|trust the contents",
        TrustAcceptInput = "\r",
    };
}
