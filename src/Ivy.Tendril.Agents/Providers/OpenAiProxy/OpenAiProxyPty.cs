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
    public IReadOnlyList<AgentProfileDefault> DefaultProfiles
    {
        get
        {
            var baseUrl = _baseUrlProvider();
            if (baseUrl != null && baseUrl.Contains("llmproxy.ivy.app"))
            {
                return
                [
                    new AgentProfileDefault(ProfileTier.Deep, "claude-opus-5", "max"),
                    new AgentProfileDefault(ProfileTier.Balanced, "gemini-3.7-flash", "medium"),
                    new AgentProfileDefault(ProfileTier.Quick, "gemini-3.7-flash", "low"),
                ];
            }
            if (baseUrl != null && baseUrl.Contains("api.anthropic.com"))
            {
                return
                [
                    new AgentProfileDefault(ProfileTier.Deep, "claude-opus-5", "max"),
                    new AgentProfileDefault(ProfileTier.Balanced, "claude-sonnet-5", "high"),
                    new AgentProfileDefault(ProfileTier.Quick, "claude-haiku-5", "low"),
                ];
            }
            if (baseUrl != null && (baseUrl.Contains("generativelanguage.googleapis.com") || baseUrl.Contains("gemini") || baseUrl.Contains("google")))
            {
                return
                [
                    new AgentProfileDefault(ProfileTier.Deep, "gemini-3.7-flash", "high"),
                    new AgentProfileDefault(ProfileTier.Balanced, "gemini-3.7-flash", "medium"),
                    new AgentProfileDefault(ProfileTier.Quick, "gemini-3.7-flash", "medium"),
                ];
            }
            if (baseUrl != null && baseUrl.Contains("api.berget.ai"))
            {
                return
                [
                    new AgentProfileDefault(ProfileTier.Deep, "moonshotai/Kimi-K3", "max"),
                    new AgentProfileDefault(ProfileTier.Balanced, "moonshotai/Kimi-K3", "high"),
                    new AgentProfileDefault(ProfileTier.Quick, "moonshotai/Kimi-K3", "low"),
                ];
            }
            return
            [
                new AgentProfileDefault(ProfileTier.Deep, "gpt-5.6-sol", "high"),
                new AgentProfileDefault(ProfileTier.Balanced, "gpt-5.6-terra", "medium"),
                new AgentProfileDefault(ProfileTier.Quick, "gpt-5.6-luna", "low"),
            ];
        }
    }
    public string? ContextFileName => _inner.ContextFileName;

    public string? TranslateToolName(string canonicalTool) => _inner.TranslateToolName(canonicalTool);
    public string? ReverseTranslateToolName(string nativeTool) => _inner.ReverseTranslateToolName(nativeTool);
    public IReadOnlyList<string> ExtractWritableDirectories(IReadOnlyList<string> allowedTools) => _inner.ExtractWritableDirectories(allowedTools);

    public AgentPtySpec BuildPtySpec(AgentPtyConfig config)
    {
        var baseUrl = _baseUrlProvider();
        var model = config.Model;
        if (string.IsNullOrEmpty(model) || model == "default")
        {
            if (baseUrl != null && baseUrl.Contains("llmproxy.ivy.app"))
            {
                config = config with { Model = "claude-opus-5" };
            }
            else if (baseUrl != null && baseUrl.Contains("api.anthropic.com"))
            {
                config = config with { Model = "claude-sonnet-5" };
            }
            else if (baseUrl != null && (baseUrl.Contains("generativelanguage.googleapis.com") || baseUrl.Contains("gemini") || baseUrl.Contains("google")))
            {
                config = config with { Model = "gemini-3.7-flash" };
            }
            else if (baseUrl != null && baseUrl.Contains("api.berget.ai"))
            {
                config = config with { Model = "moonshotai/Kimi-K3" };
            }
            else
            {
                config = config with { Model = "gpt-5.6-terra" };
            }
        }

        var spec = _inner.BuildPtySpec(config);

        var env = new Dictionary<string, string>(spec.Environment);
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
