using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.OpenCode;

namespace Ivy.Tendril.Agents.Providers.Ivy;

public sealed class IvyPty : IAgentPty
{
    private readonly Func<string?> _apiKeyProvider;
    private readonly OpenCodePty _inner = new();

    public IvyPty(Func<string?>? apiKeyProvider = null)
    {
        _apiKeyProvider = apiKeyProvider ?? (() => null);
    }

    public string Id => Abstractions.AgentId.Ivy;
    public string DisplayName => "Ivy Agent";

    public AgentCapabilities Capabilities => _inner.Capabilities;
    public TransportKind SupportedTransports => _inner.SupportedTransports;
    public IReadOnlyList<AgentProfileDefault> DefaultProfiles => _inner.DefaultProfiles;
    public string? ContextFileName => _inner.ContextFileName;

    public string? TranslateToolName(string canonicalTool) => _inner.TranslateToolName(canonicalTool);
    public string? ReverseTranslateToolName(string nativeTool) => _inner.ReverseTranslateToolName(nativeTool);
    public IReadOnlyList<string> ExtractWritableDirectories(IReadOnlyList<string> allowedTools) => _inner.ExtractWritableDirectories(allowedTools);

    public AgentPtySpec BuildPtySpec(AgentPtyConfig config)
    {
        var spec = _inner.BuildPtySpec(config);

        var env = new Dictionary<string, string>(spec.Environment);
        env["ANTHROPIC_BASE_URL"] = "https://llmproxy.ivy.app";

        var apiKey = _apiKeyProvider();
        if (!string.IsNullOrEmpty(apiKey))
        {
            env["ANTHROPIC_API_KEY"] = apiKey;
        }

        return new AgentPtySpec
        {
            CommandLine = spec.CommandLine,
            WorkingDirectory = spec.WorkingDirectory,
            Environment = env,
        };
    }

    public IReadOnlyDictionary<string, string> GetDefaultEnvironment()
    {
        var env = new Dictionary<string, string>(_inner.GetDefaultEnvironment());
        env["ANTHROPIC_BASE_URL"] = "https://llmproxy.ivy.app";
        return env;
    }

    public AgentActivityPatterns? GetActivityPatterns() => _inner.GetActivityPatterns();
}
