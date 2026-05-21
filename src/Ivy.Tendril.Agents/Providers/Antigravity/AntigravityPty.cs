using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Providers.Antigravity;

public sealed class AntigravityPty : IAgentPty
{
    public string Id => AgentId.Antigravity;
    public string DisplayName => "Antigravity";

    public AgentCapabilities Capabilities =>
        AgentCapabilities.DirectoryRestriction |
        AgentCapabilities.HealthCheck |
        AgentCapabilities.ExtraArgPassthrough |
        AgentCapabilities.SessionResume;

    public TransportKind SupportedTransports => TransportKind.Pty;

    public string? TranslateToolName(string canonicalTool) => null;

    public string? ReverseTranslateToolName(string nativeTool) => null;

    public IReadOnlyList<string> ExtractWritableDirectories(IReadOnlyList<string> allowedTools) => [];

    public IReadOnlyDictionary<string, string> GetDefaultEnvironment() =>
        new Dictionary<string, string>
        {
            ["TERM"] = "xterm-256color",
        };

    public AgentPtySpec BuildPtySpec(AgentPtyConfig config)
    {
        var args = new List<string> { "agy" };

        if (!string.IsNullOrEmpty(config.SessionId))
        {
            args.Add("--conversation");
            args.Add(config.SessionId);
        }

        if (config.PermissionMode == PermissionMode.FullAuto)
            args.Add("--dangerously-skip-permissions");

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
        WorkingPattern = @"⠋|⠙|⠹|⠸|⠼|⠴|⠦|⠧|⠇|⠏|●|Thinking",
        IdlePattern = @">\s*$",
        ErrorPattern = @"Error:|error:|ERR!|not logged in",
    };
}
