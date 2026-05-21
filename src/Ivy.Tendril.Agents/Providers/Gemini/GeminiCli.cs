using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Providers.Gemini;

public sealed class GeminiCli : IAgentCli
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

    public TransportKind SupportedTransports => TransportKind.CliSpawn;
    public PromptTransport PromptTransport => PromptTransport.Stdin;
    public OutputFormat PreferredOutputFormat => OutputFormat.Json;

    private static readonly Regex WritableDirRegex = new(
        @"^dir:(.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string? TranslateToolName(string canonicalTool) => null;

    public string? ReverseTranslateToolName(string nativeTool) => null;

    public IReadOnlyList<string> ExtractWritableDirectories(IReadOnlyList<string> allowedTools)
    {
        var dirs = new List<string>();
        foreach (var tool in allowedTools)
        {
            var match = WritableDirRegex.Match(tool);
            if (match.Success)
                dirs.Add(match.Groups[1].Value);
        }
        return dirs;
    }

    public AgentProcessSpec BuildProcessSpec(AgentLaunchConfig config)
    {
        var approvalMode = DetermineApprovalMode(config.AllowedTools);

        var args = new List<string>
        {
            "--approval-mode", approvalMode,
            "--skip-trust",
            "--output-format", "json",
        };

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

        foreach (var dir in config.WritableDirectories)
        {
            args.Add("--include-directories");
            args.Add(dir);
        }

        var extractedDirs = ExtractWritableDirectories(config.AllowedTools);
        foreach (var dir in extractedDirs)
        {
            args.Add("--include-directories");
            args.Add(dir);
        }

        foreach (var arg in config.ExtraArguments)
            args.Add(arg);

        // --prompt " " triggers headless stdin reading mode
        args.Add("--prompt");
        args.Add(" ");

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

    private static string DetermineApprovalMode(IReadOnlyList<string> allowedTools)
    {
        if (allowedTools.Count == 0)
            return "yolo";

        foreach (var tool in allowedTools)
        {
            if (tool.StartsWith("Write", StringComparison.OrdinalIgnoreCase) ||
                tool.StartsWith("Edit", StringComparison.OrdinalIgnoreCase))
                return "yolo";
        }

        return "plan";
    }
}
