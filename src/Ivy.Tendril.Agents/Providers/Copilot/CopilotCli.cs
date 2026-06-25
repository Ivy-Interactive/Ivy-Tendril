using System.Collections.Frozen;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;

namespace Ivy.Tendril.Agents.Providers.Copilot;

public sealed class CopilotCli : IAgentCli
{
    public string Id => AgentId.Copilot;
    public string DisplayName => "GitHub Copilot";

    public AgentCapabilities Capabilities =>
        AgentCapabilities.StreamJsonOutput |
        AgentCapabilities.ModelSelection |
        AgentCapabilities.EffortControl |
        AgentCapabilities.DirectoryRestriction |
        AgentCapabilities.HealthCheck |
        AgentCapabilities.ExtraArgPassthrough;

    public TransportKind SupportedTransports => TransportKind.CliSpawn;
    public PromptTransport PromptTransport => PromptTransport.Stdin;
    public OutputFormat PreferredOutputFormat => OutputFormat.StreamJson;

    public IReadOnlyList<AgentProfileDefault> DefaultProfiles { get; } =
    [
        new(ProfileTier.Deep, null, "high"),
        new(ProfileTier.Balanced, null, "medium"),
        new(ProfileTier.Quick, null, "low"),
    ];

    private static readonly string ShellToolName =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "powershell" : "bash";

    private static readonly FrozenDictionary<string, string> ToolNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [CanonicalTools.Read] = "view",
        [CanonicalTools.Write] = "apply_patch",
        [CanonicalTools.Edit] = "apply_patch",
        [CanonicalTools.Bash] = ShellToolName,
        [CanonicalTools.Glob] = "glob",
        [CanonicalTools.Grep] = "rg",
        [CanonicalTools.WebFetch] = "web_fetch",
        [CanonicalTools.WebSearch] = "web_fetch",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string> ReverseToolNameMap =
        ToolNameMap
            .GroupBy(kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly Regex WritableDirPattern = new(
        @"(?:Write|Edit|apply_patch)\s*[:=]\s*(.+?)(?:/\*\*?|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string? TranslateToolName(string canonicalTool)
        => ToolNameMap.GetValueOrDefault(canonicalTool);

    public string? ReverseTranslateToolName(string nativeTool)
        => ReverseToolNameMap.GetValueOrDefault(nativeTool);

    public IReadOnlyList<string> ExtractWritableDirectories(IReadOnlyList<string> allowedTools)
    {
        var dirs = new List<string>();
        foreach (var tool in allowedTools)
        {
            var match = WritableDirPattern.Match(tool);
            if (match.Success)
            {
                var dir = match.Groups[1].Value.TrimEnd('/', '\\', '*');
                if (!string.IsNullOrWhiteSpace(dir))
                    dirs.Add(dir);
            }
        }
        return dirs;
    }

    public AgentProcessSpec BuildProcessSpec(AgentLaunchConfig config)
    {
        var args = new List<string>
        {
            "--allow-all-paths",
            "--allow-all-urls",
            "--output-format", "json",
            "-s"
        };

        if (config.AllowedTools.Count > 0)
        {
            var translated = config.AllowedTools
                .Select(t => TranslateToolName(t) ?? t)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            args.Add("--available-tools");
            args.Add(string.Join(",", translated));
            args.Add("--allow-all-tools");
        }

        if (!string.IsNullOrEmpty(config.Model))
        {
            args.Add("--model");
            args.Add(config.Model);
        }

        if (config.Effort is not null)
        {
            args.Add("--effort");
            args.Add(config.Effort.Value switch
            {
                EffortLevel.Low => "low",
                EffortLevel.Medium => "medium",
                EffortLevel.High => "high",
                EffortLevel.XHigh => "xhigh",
                EffortLevel.Max => "xhigh",
                _ => "medium"
            });
        }

        if (!string.IsNullOrEmpty(config.SessionId))
        {
            args.Add("--name");
            args.Add(config.SessionId);
        }

        var writableDirs = ExtractWritableDirectories(config.AllowedTools);
        foreach (var dir in config.WritableDirectories.Concat(writableDirs).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            args.Add("--add-dir");
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

        var (fileName, prefixArgs) = CopilotBinaryResolver.Resolve();

        return new AgentProcessSpec
        {
            FileName = fileName,
            Arguments = [.. prefixArgs, .. args],
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
            ["TERM"] = "dumb"
        };
}
