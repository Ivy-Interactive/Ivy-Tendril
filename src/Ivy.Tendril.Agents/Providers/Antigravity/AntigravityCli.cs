using System;
using System.Collections.Generic;
using System.IO;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Providers.Antigravity;

public sealed class AntigravityCli : IAgentCli
{
    public string Id => AgentId.Antigravity;
    public string DisplayName => "Antigravity";

    public AgentCapabilities Capabilities =>
        AgentCapabilities.StdinPrompt |
        AgentCapabilities.StreamJsonOutput |
        AgentCapabilities.ModelSelection |
        AgentCapabilities.EffortControl |
        AgentCapabilities.DirectoryRestriction |
        AgentCapabilities.HealthCheck |
        AgentCapabilities.ExtraArgPassthrough |
        AgentCapabilities.SessionResume;

    public TransportKind SupportedTransports => TransportKind.CliSpawn;
    public PromptTransport PromptTransport => PromptTransport.Stdin;
    public OutputFormat PreferredOutputFormat => OutputFormat.StreamJson;

    public IReadOnlyList<AgentProfileDefault> DefaultProfiles { get; } =
    [
        new(ProfileTier.Deep, "gemini-3.6-flash", "medium"),
        new(ProfileTier.Balanced, "gemini-3.6-flash", "medium"),
        new(ProfileTier.Quick, "gemini-3.6-flash", "medium"),
    ];

    public string? TranslateToolName(string canonicalTool) => null;

    public string? ReverseTranslateToolName(string nativeTool) => null;

    public IReadOnlyList<string> ExtractWritableDirectories(IReadOnlyList<string> allowedTools) => [];

    public AgentProcessSpec BuildProcessSpec(AgentLaunchConfig config)
    {
        var args = new List<string>
        {
            "--dangerously-skip-permissions",
            "--output-format", "stream-json",
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
        if (!string.IsNullOrEmpty(config.PromptFilePath))
        {
            var normalizedPath = config.PromptFilePath.Replace('\\', '/');
            args.Add($"@{normalizedPath}");
        }
        else
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"tendril-agy-prompt-{Guid.NewGuid():N}.md");
            File.WriteAllText(tempFile, config.Prompt);
            var normalizedTemp = tempFile.Replace('\\', '/');
            args.Add($"@{normalizedTemp}");
        }

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
            RedirectStdin = false,
        };
    }

    public IReadOnlyDictionary<string, string> GetDefaultEnvironment() =>
        new Dictionary<string, string>
        {
            ["CI"] = "true",
            ["TERM"] = "dumb",
        };
}
