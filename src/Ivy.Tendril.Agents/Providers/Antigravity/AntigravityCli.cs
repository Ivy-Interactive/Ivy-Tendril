using System.Collections.Generic;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Providers.Antigravity;

public sealed class AntigravityCli : IAgentCli
{
    public string Id => AgentId.Antigravity;
    public string DisplayName => "Antigravity";

    public AgentCapabilities Capabilities =>
        AgentCapabilities.StdinPrompt |
        AgentCapabilities.DirectoryRestriction |
        AgentCapabilities.HealthCheck |
        AgentCapabilities.ExtraArgPassthrough |
        AgentCapabilities.SessionResume;

    public TransportKind SupportedTransports => TransportKind.CliSpawn;
    public PromptTransport PromptTransport => PromptTransport.Stdin;
    public OutputFormat PreferredOutputFormat => OutputFormat.Text;

    public IReadOnlyList<AgentProfileDefault> DefaultProfiles { get; } =
    [
        new(ProfileTier.Deep, "default", null),
        new(ProfileTier.Balanced, "default", null),
        new(ProfileTier.Quick, "default", null),
    ];

    public string? TranslateToolName(string canonicalTool) => null;

    public string? ReverseTranslateToolName(string nativeTool) => null;

    public IReadOnlyList<string> ExtractWritableDirectories(IReadOnlyList<string> allowedTools) => [];

    public AgentProcessSpec BuildProcessSpec(AgentLaunchConfig config)
    {
        var args = new List<string>
        {
            "--dangerously-skip-permissions",
        };

        if (!string.IsNullOrEmpty(config.Model))
        {
            args.Add("--model");
            args.Add(config.Model);

            var effort = config.Effort switch
            {
                EffortLevel.Low => "low",
                EffortLevel.High => "high",
                EffortLevel.XHigh => "high",
                _ => "medium"
            };
            args.Add("--effort");
            args.Add(effort);
        }

        if (!string.IsNullOrEmpty(config.SessionId))
        {
            args.Add("--conversation");
            args.Add(config.SessionId);
        }

        foreach (var dir in config.WritableDirectories)
        {
            args.Add("--add-dir");
            args.Add(dir);
        }

        foreach (var arg in config.ExtraArguments)
            args.Add(arg);

        args.Add("--print");
        args.Add(config.Prompt);

        var env = new Dictionary<string, string>(GetDefaultEnvironment());
        if (config.EnvironmentVariables is not null)
        {
            foreach (var (key, value) in config.EnvironmentVariables)
                env[key] = value;
        }

        return new AgentProcessSpec
        {
            FileName = "agy",
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
        };
}
