namespace Ivy.Tendril.Agents.Abstractions;

public sealed record AgentLaunchConfig
{
    public required string Prompt { get; init; }
    public required string WorkingDirectory { get; init; }
    public string? Model { get; init; }
    public EffortLevel? Effort { get; init; }
    public PermissionMode PermissionMode { get; init; } = PermissionMode.FullAuto;
    public IReadOnlyList<string> AllowedTools { get; init; } = [];
    public IReadOnlyList<string> DeniedTools { get; init; } = [];
    public IReadOnlyList<string> WritableDirectories { get; init; } = [];
    public string? SessionId { get; init; }
    public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; init; }
    public IReadOnlyList<string> ExtraArguments { get; init; } = [];
    public string? SystemPrompt { get; init; }
    public int? MaxTurns { get; init; }
    public decimal? MaxBudgetUsd { get; init; }
    public TimeSpan? Timeout { get; init; }
    public OutputFormat OutputFormat { get; init; } = OutputFormat.StreamJson;
    public string? PromptFilePath { get; init; }
    public IReadOnlyList<McpServerConfig> McpServers { get; init; } = [];
}

public sealed record McpServerConfig(
    string Name,
    string Command,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string>? Environment = null);

public sealed record AgentProcessSpec
{
    public required string FileName { get; init; }
    public required IReadOnlyList<string> Arguments { get; init; }
    public required string WorkingDirectory { get; init; }
    public required IReadOnlyDictionary<string, string> Environment { get; init; }
    public string? StdinContent { get; init; }
    public bool RedirectStdin { get; init; }
    public bool RedirectStdout { get; init; } = true;
    public bool RedirectStderr { get; init; } = true;
    public bool CreateNoWindow { get; init; } = true;
    public bool UseShellExecute { get; init; }
}
