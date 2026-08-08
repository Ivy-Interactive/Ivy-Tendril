namespace Ivy.Tendril.Agents.Abstractions;

public readonly record struct AgentId(string Value)
{
    public static readonly AgentId Antigravity = new("antigravity");
    public static readonly AgentId Claude = new("claude");
    public static readonly AgentId Codex = new("codex");
    public static readonly AgentId Copilot = new("copilot");
    public static readonly AgentId Gemini = new("gemini");
    public static readonly AgentId OpenCode = new("opencode");
    public static readonly AgentId Ivy = new("ivy");
    public static readonly AgentId OpenAiProxy = new("openaiproxy");
    public static readonly AgentId Berget = new("berget");

    public static implicit operator string(AgentId agentId) => agentId.Value ?? "";
    public static implicit operator AgentId(string? value) => new(value ?? "");
    public override string ToString() => Value ?? "";
}

public static class CanonicalTools
{
    public const string Read = "Read";
    public const string Write = "Write";
    public const string Edit = "Edit";
    public const string Bash = "Bash";
    public const string Glob = "Glob";
    public const string Grep = "Grep";
    public const string WebFetch = "WebFetch";
    public const string WebSearch = "WebSearch";
}

public enum PromptTransport
{
    Stdin,
    Argument,
    File
}

public enum EffortLevel
{
    Low,
    Medium,
    High,
    XHigh,
    Max
}

public enum PermissionMode
{
    Default,
    AcceptEdits,
    FullAuto,
    Plan
}

public enum OutputFormat
{
    StreamJson,
    Json,
    Text
}

public enum AuthStatus
{
    Authenticated,
    NotAuthenticated,
    Unknown,
    CheckFailed
}

public enum SessionState
{
    NotStarted,
    Starting,
    Running,
    Idle,
    Blocked,
    Completed,
    Failed,
    TimedOut,
    Stopped
}

[Flags]
public enum AgentCapabilities
{
    None = 0,
    StdinPrompt = 1 << 0,
    ArgumentPrompt = 1 << 1,
    StreamJsonOutput = 1 << 2,
    JsonOutput = 1 << 3,
    CostInOutput = 1 << 4,
    ModelSelection = 1 << 5,
    EffortControl = 1 << 6,
    ToolAllowlisting = 1 << 7,
    DirectoryRestriction = 1 << 8,
    SessionResume = 1 << 9,
    SessionFork = 1 << 10,
    PermissionDenialReporting = 1 << 11,
    GranularPermissions = 1 << 12,
    WorkingDirectoryFlag = 1 << 13,
    HealthCheck = 1 << 14,
    ExtraArgPassthrough = 1 << 15,
    MaxTurns = 1 << 16,
}

[Flags]
public enum TransportKind
{
    CliSpawn = 1 << 0,
    PersistentServer = 1 << 1,
    Sdk = 1 << 2,
    Pty = 1 << 3,
}

public enum PermissionScope
{
    Once,
    Session,
    Always
}

public enum ResponseSource
{
    Desktop,
    Mobile,
    Api,
    Automation
}

public enum ModelValidationStatus
{
    Ok,
    InvalidModel,
    AuthError,
    Timeout,
    Unknown
}
