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
        AgentCapabilities.CostInOutput |
        AgentCapabilities.ExtraArgPassthrough;

    public TransportKind SupportedTransports => TransportKind.CliSpawn;
    public PromptTransport PromptTransport => PromptTransport.Stdin;
    public OutputFormat PreferredOutputFormat => OutputFormat.StreamJson;

    public IReadOnlyList<AgentProfileDefault> DefaultProfiles { get; } =
    [
        new(ProfileTier.Deep, "gemini-3.7-flash", "medium"),
        new(ProfileTier.Balanced, "gemini-3.7-flash", "medium"),
        new(ProfileTier.Quick, "gemini-3.7-flash", "medium"),
    ];

    public IReadOnlyList<EffortOption> SupportedEfforts => EffortLevels.Antigravity;

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

        if (config.Timeout is { } timeout && timeout > TimeSpan.Zero)
        {
            args.Add("--print-timeout");
            args.Add($"{(int)timeout.TotalSeconds}s");
        }

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

        // Note: agy --conversation only accepts an existing conversation ID that agy already stored in
        // ~/.gemini/antigravity-cli/conversations/*.pb. AgentLaunchConfig.SessionId is a per-job GUID
        // minted by JobLauncher.PrepareJobForLaunch, so passing it would cause agy to print
        // "warning: conversation <id> not found" on every run. See GitHub issue #2074.

        foreach (var dir in config.WritableDirectories)
        {
            args.Add("--add-dir");
            args.Add(dir);
        }

        var tempFiles = new List<string>();

        var mcpConfigFile = global::Ivy.Tendril.Agents.Helpers.McpConfigWriter.WriteConfigFile(config.McpServers);
        if (!string.IsNullOrEmpty(mcpConfigFile))
        {
            tempFiles.Add(mcpConfigFile);
            args.Add("--mcp-config");
            args.Add(mcpConfigFile);
        }

        foreach (var arg in config.ExtraArguments)
            args.Add(arg);

        var finalPrompt = !string.IsNullOrEmpty(config.SystemPrompt)
            ? config.SystemPrompt + "\n\n---\n\n" + config.Prompt
            : config.Prompt;

        args.Add("--print");
        if (!string.IsNullOrEmpty(config.PromptFilePath) && string.IsNullOrEmpty(config.SystemPrompt))
        {
            var normalizedPath = config.PromptFilePath.Replace('\\', '/');
            args.Add($"@{normalizedPath}");
        }
        else
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"tendril-agy-prompt-{Guid.NewGuid():N}.md");
            File.WriteAllText(tempFile, finalPrompt);
            tempFiles.Add(tempFile);
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
            StdinContent = null,
            RedirectStdin = false,
            TempFiles = tempFiles,
        };
    }

    public IReadOnlyDictionary<string, string> GetDefaultEnvironment() =>
        new Dictionary<string, string>
        {
            ["CI"] = "true",
            ["TERM"] = "dumb",
        };
}
