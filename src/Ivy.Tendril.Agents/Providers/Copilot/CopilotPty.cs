using System.Collections.Frozen;
using System.Runtime.InteropServices;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Providers.Copilot;

public sealed class CopilotPty : IAgentPty
{
    public string Id => AgentId.Copilot;
    public string DisplayName => "Copilot";

    public AgentCapabilities Capabilities =>
        AgentCapabilities.ArgumentPrompt |
        AgentCapabilities.StreamJsonOutput |
        AgentCapabilities.ModelSelection |
        AgentCapabilities.EffortControl |
        AgentCapabilities.DirectoryRestriction |
        AgentCapabilities.HealthCheck |
        AgentCapabilities.ExtraArgPassthrough;

    public TransportKind SupportedTransports => TransportKind.Pty;
    public IReadOnlyList<AgentProfileDefault> DefaultProfiles => [];

    public string? ContextFileName => "AGENTS.md";

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

    public string? TranslateToolName(string canonicalTool)
        => ToolNameMap.GetValueOrDefault(canonicalTool);

    public string? ReverseTranslateToolName(string nativeTool)
        => ReverseToolNameMap.GetValueOrDefault(nativeTool);

    public IReadOnlyList<string> ExtractWritableDirectories(IReadOnlyList<string> allowedTools) => [];

    public IReadOnlyDictionary<string, string> GetDefaultEnvironment() =>
        new Dictionary<string, string>
        {
            ["TERM"] = "xterm-256color",
        };

    public AgentPtySpec BuildPtySpec(AgentPtyConfig config)
    {
        var (fileName, prefixArgs) = CopilotBinaryResolver.Resolve();
        var args = new List<string> { fileName };
        args.AddRange(prefixArgs);

        // FullAuto → grant all tools, paths, and URLs without prompting.
        if (config.PermissionMode == PermissionMode.FullAuto)
        {
            args.Add("--allow-all-paths");
            args.Add("--allow-all-urls");
            args.Add("--allow-all-tools");
        }

        if (!string.IsNullOrEmpty(config.Model))
        {
            args.Add("--model");
            args.Add(config.Model);
        }

        // Initial task: -i starts interactive mode AND auto-executes the prompt
        // (the first-run trust modal, if any, is handled by the host before it runs).
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
        WorkingPattern = @"⠋|⠙|⠹|⠸|⠼|⠴|⠦|⠧|⠇|⠏|●|working|thinking",
        IdlePattern = @">\s*$",
        ErrorPattern = @"Error:|error:|ERR!",
        PermissionPromptPattern = @"Allow|Deny|approve|reject",
        // First-run "Confirm folder trust" modal; default selection is "Yes" → Enter.
        TrustPromptPattern = @"trust the files|Do you trust|Confirm folder trust",
        TrustAcceptInput = "\r",
    };
}
