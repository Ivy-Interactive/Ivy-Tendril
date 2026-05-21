namespace Ivy.Tendril.Agents.Abstractions;

public sealed record AgentResolutionContext
{
    public string? AgentId { get; init; }
    public required string Prompt { get; init; }
    public required string WorkingDirectory { get; init; }
    public string? Profile { get; init; }
    public string? ModelOverride { get; init; }
    public EffortLevel? EffortOverride { get; init; }
    public string? SessionId { get; init; }
    public IReadOnlyDictionary<string, string>? ExtraEnvironment { get; init; }
    public string? PromptFilePath { get; init; }
    public TransportKind? PreferredTransport { get; init; }
    public IReadOnlyDictionary<string, string>? Variables { get; init; }
    public SessionMetadata? Metadata { get; init; }
    public TimeoutPolicy? TimeoutPolicy { get; init; }
    public IInteractionHandler? InteractionHandler { get; init; }
    public string? RecordingBasePath { get; init; }
    public PermissionMode PermissionMode { get; init; } = PermissionMode.FullAuto;
    public IReadOnlyList<string> AllowedTools { get; init; } = [];
    public IReadOnlyList<string> DeniedTools { get; init; } = [];
    public IReadOnlyList<string> ExtraArguments { get; init; } = [];
    public int? MaxTurns { get; init; }
    public decimal? MaxBudgetUsd { get; init; }
    public IReadOnlyList<McpServerConfig> McpServers { get; init; } = [];
    public IRetryPolicy? RetryPolicy { get; init; }
}
