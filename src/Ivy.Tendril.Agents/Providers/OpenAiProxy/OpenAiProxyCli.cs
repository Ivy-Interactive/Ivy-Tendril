using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Ivy;
using Ivy.Tendril.Agents.Providers.OpenCode;

namespace Ivy.Tendril.Agents.Providers.OpenAiProxy;

public sealed class OpenAiProxyCli : IAgentCli
{
    private readonly OpenCodeCli _inner = new();
    private readonly Func<string?> _apiKeyProvider;
    private readonly Func<string?> _baseUrlProvider;

    public OpenAiProxyCli(Func<string?>? apiKeyProvider = null, Func<string?>? baseUrlProvider = null)
    {
        _apiKeyProvider = apiKeyProvider ?? (() => null);
        _baseUrlProvider = baseUrlProvider ?? (() => null);
    }

    public string Id => Abstractions.AgentId.OpenAiProxy;
    public string DisplayName => "OpenAI Proxy";

    public AgentCapabilities Capabilities => _inner.Capabilities;
    public TransportKind SupportedTransports => _inner.SupportedTransports;
    public PromptTransport PromptTransport => _inner.PromptTransport;
    public OutputFormat PreferredOutputFormat => _inner.PreferredOutputFormat;
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

    public IReadOnlyList<EffortOption> SupportedEfforts => _inner.SupportedEfforts;

    public string? TranslateToolName(string canonicalTool) => _inner.TranslateToolName(canonicalTool);
    public string? ReverseTranslateToolName(string nativeTool) => _inner.ReverseTranslateToolName(nativeTool);
    public IReadOnlyList<string> ExtractWritableDirectories(IReadOnlyList<string> allowedTools) => _inner.ExtractWritableDirectories(allowedTools);

    public AgentProcessSpec BuildProcessSpec(AgentLaunchConfig config)
    {
        var baseUrl = _baseUrlProvider();
        var model = config.Model;
        if (string.IsNullOrEmpty(model) || model == "default")
        {
            if (baseUrl != null && baseUrl.Contains("llmproxy.ivy.app"))
            {
                model = "claude-opus-5";
            }
            else if (baseUrl != null && baseUrl.Contains("api.anthropic.com"))
            {
                model = "claude-sonnet-5";
            }
            else if (baseUrl != null && (baseUrl.Contains("generativelanguage.googleapis.com") || baseUrl.Contains("gemini") || baseUrl.Contains("google")))
            {
                model = "gemini-3.7-flash";
            }
            else if (baseUrl != null && baseUrl.Contains("api.berget.ai"))
            {
                model = "moonshotai/Kimi-K3";
            }
            else
            {
                model = "gpt-5.6-terra";
            }
        }

        config = config with { Model = model };

        var spec = _inner.BuildProcessSpec(config);

        var env = new Dictionary<string, string>(spec.Environment);
        if (!string.IsNullOrEmpty(baseUrl))
        {
            env["OPENAI_BASE_URL"] = baseUrl;
            env["ANTHROPIC_BASE_URL"] = baseUrl;
        }

        var apiKey = _apiKeyProvider();
        if (!string.IsNullOrEmpty(apiKey))
        {
            env["OPENAI_API_KEY"] = apiKey;
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
        var baseUrl = _baseUrlProvider();
        if (!string.IsNullOrEmpty(baseUrl))
        {
            env["OPENAI_BASE_URL"] = baseUrl;
            env["ANTHROPIC_BASE_URL"] = baseUrl;
        }
        var apiKey = _apiKeyProvider();
        if (!string.IsNullOrEmpty(apiKey))
        {
            env["OPENAI_API_KEY"] = apiKey;
            env["ANTHROPIC_API_KEY"] = apiKey;
        }
        return env;
    }
}
