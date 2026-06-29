namespace Ivy.Tendril.Agents.Abstractions;

public interface IAgentPty : IAgentDescriptor
{
    AgentPtySpec BuildPtySpec(AgentPtyConfig config);
    AgentActivityPatterns? GetActivityPatterns();

    /// <summary>
    /// File the agent reads from the working directory for project/system instructions
    /// (e.g. "AGENTS.md", "GEMINI.md"). Null when the agent takes its system prompt via a
    /// command-line flag instead (Claude uses --append-system-prompt-file).
    /// </summary>
    string? ContextFileName { get; }
}

public sealed record AgentPtyConfig
{
    public required string WorkingDirectory { get; init; }
    public string? Model { get; init; }
    public string? SystemPrompt { get; init; }
    public bool AppendSystemPrompt { get; init; }
    public PermissionMode PermissionMode { get; init; } = PermissionMode.FullAuto;
    public string? SessionId { get; init; }

    /// <summary>
    /// Initial task to deliver to the agent. Passed as a command-line argument (positional or
    /// via the agent's interactive-prompt flag) so the agent auto-runs it on launch — avoids the
    /// fragile "paste into the TUI" approach and Windows arg-quoting issues (argv is an array).
    /// </summary>
    public string? InitialPrompt { get; init; }

    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<string> ExtraArguments { get; init; } = [];
    public IReadOnlyList<McpServerConfig> McpServers { get; init; } = [];
    public int Cols { get; init; } = 120;
    public int Rows { get; init; } = 40;
}

public sealed record AgentPtySpec
{
    public required IReadOnlyList<string> CommandLine { get; init; }
    public required string WorkingDirectory { get; init; }
    public required IReadOnlyDictionary<string, string> Environment { get; init; }
}

public sealed record AgentActivityPatterns
{
    public string? WorkingPattern { get; init; }
    public string? IdlePattern { get; init; }
    public string? ErrorPattern { get; init; }
    public string? PermissionPromptPattern { get; init; }

    /// <summary>
    /// Regex matching a first-run "trust this folder?" interstitial that intercepts input before
    /// the agent accepts the prompt. When set, the host should send <see cref="TrustAcceptInput"/>
    /// once on first match to dismiss it. Null for agents that have no such modal.
    /// </summary>
    public string? TrustPromptPattern { get; init; }

    /// <summary>Keystroke(s) that accept the trust prompt (default: Enter on the highlighted "Yes").</summary>
    public string? TrustAcceptInput { get; init; }
}
