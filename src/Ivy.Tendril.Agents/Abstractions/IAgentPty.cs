namespace Ivy.Tendril.Agents.Abstractions;

public interface IAgentPty : IAgentDescriptor
{
    AgentPtySpec BuildPtySpec(AgentPtyConfig config);
    AgentActivityPatterns? GetActivityPatterns();
}

public sealed record AgentPtyConfig
{
    public required string WorkingDirectory { get; init; }
    public string? Model { get; init; }
    public string? SystemPrompt { get; init; }
    public bool AppendSystemPrompt { get; init; }
    public PermissionMode PermissionMode { get; init; } = PermissionMode.FullAuto;
    public string? SessionId { get; init; }
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
}
