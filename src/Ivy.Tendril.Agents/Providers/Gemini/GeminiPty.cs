using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Providers.Gemini;

public sealed class GeminiPty : IAgentPty
{
    public string Id => AgentId.Gemini;
    public string DisplayName => "Gemini";

    public AgentCapabilities Capabilities =>
        AgentCapabilities.ModelSelection |
        AgentCapabilities.DirectoryRestriction |
        AgentCapabilities.HealthCheck |
        AgentCapabilities.ExtraArgPassthrough;

    public TransportKind SupportedTransports => TransportKind.Pty;
    public IReadOnlyList<AgentProfileDefault> DefaultProfiles => [];

    // Gemini reads GEMINI.md (NOT AGENTS.md) for project/system instructions.
    public string? ContextFileName => "GEMINI.md";

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

        // FullAuto → auto-approve every action. The interactive TUI uses the
        // `--yolo` switch (the granular `--approval-mode` flag exists only on
        // newer headless builds, so `--yolo` is what works across versions).
        if (config.PermissionMode == PermissionMode.FullAuto)
            args.Add("--yolo");

        // Trust the workspace for this session so the first-run trust prompt never appears
        // (more explicit than GEMINI_CLI_TRUST_WORKSPACE; both are harmless together).
        args.Add("--skip-trust");

        if (!string.IsNullOrEmpty(config.Model))
        {
            args.Add("--model");
            args.Add(config.Model);
        }

        // Initial task: -i (--prompt-interactive) executes the prompt then continues interactively.
        if (!string.IsNullOrEmpty(config.InitialPrompt))
        {
            args.Add("-i");
            args.Add(config.InitialPrompt);
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
        WorkingPattern = @"⠋|⠙|⠹|⠸|⠼|⠴|⠦|⠧|⠇|⠏|●|Thinking",
        IdlePattern = @">\s*$",
        ErrorPattern = @"Error:|error:|ERR!|not logged in",
    };
}
