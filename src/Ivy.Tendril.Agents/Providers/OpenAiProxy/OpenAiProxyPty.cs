using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Ivy;
using Ivy.Tendril.Agents.Providers.OpenCode;

namespace Ivy.Tendril.Agents.Providers.OpenAiProxy;

public sealed class OpenAiProxyPty : IAgentPty
{
    private readonly Func<string?> _apiKeyProvider;
    private readonly Func<string?> _baseUrlProvider;
    private readonly OpenCodePty _inner = new();

    public OpenAiProxyPty(Func<string?>? apiKeyProvider = null, Func<string?>? baseUrlProvider = null)
    {
        _apiKeyProvider = apiKeyProvider ?? (() => null);
        _baseUrlProvider = baseUrlProvider ?? (() => null);
    }

    public string Id => Abstractions.AgentId.OpenAiProxy;
    public string DisplayName => "OpenAI Proxy";

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
        var baseUrl = _baseUrlProvider();
        if (!string.IsNullOrEmpty(baseUrl))
        {
            env["ANTHROPIC_BASE_URL"] = baseUrl;
        }

        var apiKey = _apiKeyProvider();
        if (!string.IsNullOrEmpty(apiKey))
        {
            env["ANTHROPIC_API_KEY"] = apiKey;
        }

        var args = new List<string>(spec.CommandLine);
        if (args.Count > 0 && args[0] == "opencode")
        {
            args[0] = IvyBinaryResolver.Resolve();
        }

        return new AgentPtySpec
        {
            CommandLine = args,
            WorkingDirectory = spec.WorkingDirectory,
            Environment = env,
        };
    }

    public IReadOnlyDictionary<string, string> GetDefaultEnvironment()
    {
        var env = new Dictionary<string, string>(_inner.GetDefaultEnvironment());
        var baseUrl = _baseUrlProvider();
        if (!string.IsNullOrEmpty(baseUrl))
        {
            env["ANTHROPIC_BASE_URL"] = baseUrl;
        }
        return env;
    }

    public AgentActivityPatterns? GetActivityPatterns() => _inner.GetActivityPatterns();
}
