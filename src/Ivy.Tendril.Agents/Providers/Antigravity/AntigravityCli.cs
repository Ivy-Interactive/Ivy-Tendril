using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Providers.Antigravity;

public sealed class AntigravityCli : IAgentCli
{
    public string Id => AgentId.Antigravity;
    public string DisplayName => "Antigravity";

    public AgentCapabilities Capabilities =>
        AgentCapabilities.ArgumentPrompt |
        AgentCapabilities.DirectoryRestriction |
        AgentCapabilities.HealthCheck |
        AgentCapabilities.ExtraArgPassthrough |
        AgentCapabilities.SessionResume;

    public TransportKind SupportedTransports => TransportKind.CliSpawn;
    public PromptTransport PromptTransport => PromptTransport.File;
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
        var agyArgs = new List<string>
        {
            "--dangerously-skip-permissions",
        };

        if (!string.IsNullOrEmpty(config.SessionId))
        {
            agyArgs.Add("--conversation");
            agyArgs.Add(config.SessionId);
        }

        foreach (var dir in config.WritableDirectories)
        {
            agyArgs.Add("--add-dir");
            agyArgs.Add(dir);
        }

        foreach (var arg in config.ExtraArguments)
            agyArgs.Add(arg);

        var env = new Dictionary<string, string>(GetDefaultEnvironment());
        if (config.EnvironmentVariables is not null)
        {
            foreach (var (key, value) in config.EnvironmentVariables)
                env[key] = value;
        }

        if (!string.IsNullOrEmpty(config.PromptFilePath))
        {
            var escapedPath = config.PromptFilePath.Replace("'", "''");
            var agyArgsStr = string.Join(" ", agyArgs.Select(EscapePwshArg));
            var command = $"agy --print (Get-Content -Raw '{escapedPath}') {agyArgsStr}";

            return new AgentProcessSpec
            {
                FileName = "pwsh",
                Arguments = ["-NoProfile", "-Command", command],
                WorkingDirectory = config.WorkingDirectory,
                Environment = env,
                StdinContent = null,
                RedirectStdin = false,
            };
        }

        return new AgentProcessSpec
        {
            FileName = "agy",
            Arguments = ["--print", config.Prompt, .. agyArgs],
            WorkingDirectory = config.WorkingDirectory,
            Environment = env,
            StdinContent = null,
            RedirectStdin = false,
        };
    }

    private static string EscapePwshArg(string arg)
    {
        if (arg.Contains(' ') || arg.Contains('\'') || arg.Contains('"'))
            return "'" + arg.Replace("'", "''") + "'";
        return arg;
    }

    public IReadOnlyDictionary<string, string> GetDefaultEnvironment() =>
        new Dictionary<string, string>
        {
            ["CI"] = "true",
            ["TERM"] = "dumb",
        };
}
