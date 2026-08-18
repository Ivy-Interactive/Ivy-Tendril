using System.Collections.Frozen;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.OpenCode;

namespace Ivy.Tendril.Agents.Providers.Ivy;

public sealed class IvyCli : IAgentCli
{
    private readonly Func<string?> _apiKeyProvider;
    private readonly OpenCodeCli _inner = new();

    public IvyCli(Func<string?>? apiKeyProvider = null)
    {
        _apiKeyProvider = apiKeyProvider ?? (() => null);
    }

    public string Id => Abstractions.AgentId.Ivy;
    public string DisplayName => "Ivy Agent";

    public AgentCapabilities Capabilities => _inner.Capabilities;
    public TransportKind SupportedTransports => _inner.SupportedTransports;
    public PromptTransport PromptTransport => _inner.PromptTransport;
    public OutputFormat PreferredOutputFormat => _inner.PreferredOutputFormat;
    public IReadOnlyList<AgentProfileDefault> DefaultProfiles { get; } =
    [
        new(ProfileTier.Deep, "claude-opus-5", "max"),
        new(ProfileTier.Balanced, "gemini-3.7-flash", "medium"),
        new(ProfileTier.Quick, "gemini-3.7-flash", "low"),
    ];
    public IReadOnlyList<EffortOption> SupportedEfforts => _inner.SupportedEfforts;

    public string? TranslateToolName(string canonicalTool) => _inner.TranslateToolName(canonicalTool);
    public string? ReverseTranslateToolName(string nativeTool) => _inner.ReverseTranslateToolName(nativeTool);
    public IReadOnlyList<string> ExtractWritableDirectories(IReadOnlyList<string> allowedTools) => _inner.ExtractWritableDirectories(allowedTools);

    public AgentProcessSpec BuildProcessSpec(AgentLaunchConfig config)
    {
        var model = config.Model;
        if (string.IsNullOrEmpty(model) || model == "default")
        {
            model = "claude-opus-5";
        }
        config = config with { Model = model };

        var spec = _inner.BuildProcessSpec(config);

        var env = new Dictionary<string, string>(spec.Environment);
        env["ANTHROPIC_BASE_URL"] = "https://llmproxy.ivy.app";

        var apiKey = _apiKeyProvider();
        if (!string.IsNullOrEmpty(apiKey))
        {
            env["ANTHROPIC_API_KEY"] = apiKey;
        }

        return new AgentProcessSpec
        {
            FileName = IvyBinaryResolver.Resolve(),
            Arguments = spec.Arguments,
            WorkingDirectory = spec.WorkingDirectory,
            Environment = env,
            StdinContent = spec.StdinContent,
            RedirectStdin = spec.RedirectStdin,
            RedirectStdout = spec.RedirectStdout,
            RedirectStderr = spec.RedirectStderr,
            CreateNoWindow = spec.CreateNoWindow,
            UseShellExecute = spec.UseShellExecute,
        };
    }

    public IReadOnlyDictionary<string, string> GetDefaultEnvironment()
    {
        var env = new Dictionary<string, string>(_inner.GetDefaultEnvironment());
        env["ANTHROPIC_BASE_URL"] = "https://llmproxy.ivy.app";
        return env;
    }
}
