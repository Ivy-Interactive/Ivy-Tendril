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
            if (baseUrl != null && baseUrl.Contains("api.berget.ai"))
            {
                return
                [
                    new AgentProfileDefault(ProfileTier.Deep, "kimi-k3", null),
                    new AgentProfileDefault(ProfileTier.Balanced, "kimi-k3", null),
                    new AgentProfileDefault(ProfileTier.Quick, "kimi-k3", null),
                ];
            }
            return _inner.DefaultProfiles;
        }
    }

    public string? TranslateToolName(string canonicalTool) => _inner.TranslateToolName(canonicalTool);
    public string? ReverseTranslateToolName(string nativeTool) => _inner.ReverseTranslateToolName(nativeTool);
    public IReadOnlyList<string> ExtractWritableDirectories(IReadOnlyList<string> allowedTools) => _inner.ExtractWritableDirectories(allowedTools);

    public AgentProcessSpec BuildProcessSpec(AgentLaunchConfig config)
    {
        var baseUrl = _baseUrlProvider();
        var isBerget = baseUrl?.Contains("api.berget.ai") ?? false;
        if (isBerget && string.IsNullOrEmpty(config.Model))
        {
            config = config with { Model = "kimi-k3" };
        }

        var spec = _inner.BuildProcessSpec(config);

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
            env["ANTHROPIC_BASE_URL"] = baseUrl;
        }
        return env;
    }
}
