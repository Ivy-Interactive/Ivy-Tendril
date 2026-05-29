using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Providers.Gemini;

public sealed class GeminiCli : IAgentCli
{
    public string Id => AgentId.Gemini;
    public string DisplayName => "Gemini CLI";

    public AgentCapabilities Capabilities =>
        AgentCapabilities.StdinPrompt |
        AgentCapabilities.StreamJsonOutput |
        AgentCapabilities.ModelSelection |
        AgentCapabilities.DirectoryRestriction |
        AgentCapabilities.HealthCheck |
        AgentCapabilities.ExtraArgPassthrough;

    public TransportKind SupportedTransports => TransportKind.CliSpawn;
    public PromptTransport PromptTransport => PromptTransport.Stdin;
    public OutputFormat PreferredOutputFormat => OutputFormat.StreamJson;

    public IReadOnlyList<AgentProfileDefault> DefaultProfiles { get; } =
    [
        new(ProfileTier.Deep, null, null),
        new(ProfileTier.Balanced, null, null),
        new(ProfileTier.Quick, null, null),
    ];

    public string? TranslateToolName(string canonicalTool) => null;

    public string? ReverseTranslateToolName(string nativeTool) => null;

    public IReadOnlyList<string> ExtractWritableDirectories(IReadOnlyList<string> allowedTools)
    {
        var dirs = new List<string>();
        foreach (var tool in allowedTools)
        {
            if (tool.StartsWith("dir:", StringComparison.OrdinalIgnoreCase))
                dirs.Add(tool[4..]);
        }
        return dirs;
    }

    public AgentProcessSpec BuildProcessSpec(AgentLaunchConfig config)
    {
        var args = new List<string>
        {
            "--output-format", "stream-json",
            "--skip-trust",
            "--approval-mode",
            config.PermissionMode switch
            {
                PermissionMode.FullAuto => "yolo",
                PermissionMode.AcceptEdits => "auto_edit",
                PermissionMode.Plan => "plan",
                _ => "default"
            },
            "--prompt", " ",
        };

        if (!string.IsNullOrEmpty(config.Model) && !config.Model.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--model");
            args.Add(config.Model);
        }

        foreach (var dir in config.WritableDirectories)
        {
            args.Add("--include-directories");
            args.Add(dir);
        }

        foreach (var arg in config.ExtraArguments)
            args.Add(arg);

        var env = new Dictionary<string, string>(GetDefaultEnvironment());
        if (config.EnvironmentVariables is not null)
        {
            foreach (var (key, value) in config.EnvironmentVariables)
                env[key] = value;
        }

        return new AgentProcessSpec
        {
            FileName = "gemini",
            Arguments = args,
            WorkingDirectory = config.WorkingDirectory,
            Environment = env,
            StdinContent = config.Prompt,
            RedirectStdin = true,
        };
    }

    public IReadOnlyDictionary<string, string> GetDefaultEnvironment() =>
        new Dictionary<string, string>
        {
            ["CI"] = "true",
            ["TERM"] = "dumb",
            ["GEMINI_CLI_TRUST_WORKSPACE"] = "true",
        };
}
