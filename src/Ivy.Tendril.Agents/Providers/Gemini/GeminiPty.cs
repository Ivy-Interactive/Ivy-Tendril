using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Providers.Gemini;

public sealed class GeminiPty : IAgentPty
{
    public string Id => AgentId.Gemini;
    public string DisplayName => "Gemini CLI";

    public AgentCapabilities Capabilities =>
        AgentCapabilities.StdinPrompt |
        AgentCapabilities.JsonOutput |
        AgentCapabilities.ModelSelection |
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
            ["GEMINI_CLI_TRUST_WORKSPACE"] = "true",
        };

    public AgentPtySpec BuildPtySpec(AgentPtyConfig config)
    {
        var args = new List<string> { "gemini" };

        if (!string.IsNullOrEmpty(config.Model))
        {
            args.Add("--model");
            args.Add(config.Model);
        }

        if (!string.IsNullOrEmpty(config.SessionId))
        {
            args.Add("--resume");
            args.Add(config.SessionId);
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
        IdlePattern = @">\s*$",
        ErrorPattern = @"Error:|error:|ERR!",
    };
}
