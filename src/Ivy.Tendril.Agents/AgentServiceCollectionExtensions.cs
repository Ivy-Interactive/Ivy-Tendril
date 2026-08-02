using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Antigravity;
using Ivy.Tendril.Agents.Providers.Claude;
using Ivy.Tendril.Agents.Providers.Codex;
using Ivy.Tendril.Agents.Providers.Copilot;
using Ivy.Tendril.Agents.Providers.Gemini;
using Ivy.Tendril.Agents.Providers.OpenCode;
using Ivy.Tendril.Agents.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Agents;

public sealed class AgentInfrastructureOptions
{
    public IInteractionHandler? DefaultInteractionHandler { get; set; }
    public IEnumerable<ModelPricing> AdditionalPricing { get; set; } = [];
    public ConcurrencyOptions? Concurrency { get; set; }
    public bool IncludeBetaProviders { get; set; }

    public Func<IServiceProvider, Func<string?>>? IvyApiKeyProviderFactory { get; set; }
    public Func<IServiceProvider, Func<string?>>? IvyTokenProviderFactory { get; set; }
    public Func<IServiceProvider, Func<CancellationToken, Task<string?>>>? IvyEmailProviderFactory { get; set; }

    public Func<IServiceProvider, Func<string?>>? OpenAiProxyApiKeyProviderFactory { get; set; }
    public Func<IServiceProvider, Func<string?>>? OpenAiProxyBaseUrlProviderFactory { get; set; }
}

public static class AgentServiceCollectionExtensions
{
    public static IServiceCollection AddAgentInfrastructure(
        this IServiceCollection services,
        Action<AgentInfrastructureOptions>? configure = null)
    {
        var options = new AgentInfrastructureOptions();
        configure?.Invoke(options);

        services.AddSingleton<IInteractionHandler>(
            options.DefaultInteractionHandler ?? PassthroughHandler.Instance);

        services.AddSingleton<IEventSerializer, JsonEventSerializer>();
        services.AddSingleton<AgentValidator>();
        services.AddSingleton(TimeProvider.System);

        if (options.Concurrency is not null)
            services.AddSingleton(new ConcurrencyLimiter(options.Concurrency));

        services.AddSingleton<IAgentRunner>(sp =>
        {
            var logger = sp.GetService<ILogger<AgentRunner>>() ?? NullLogger<AgentRunner>.Instance;
            var runner = new AgentRunner(logger, options.Concurrency);
            runner.Register(
                new AntigravityCli(),
                new AntigravityEventParser(),
                new AntigravityHealthCheck(),
                new AntigravityFailureAnalyzer(),
                new AntigravitySessionCostParser(),
                new AntigravityPty(),
                new AntigravityModelCatalog());
            runner.Register(
                new ClaudeCli(),
                new ClaudeEventParser(),
                new ClaudeHealthCheck(),
                new ClaudeFailureAnalyzer(),
                new ClaudeSessionCostParser(),
                new ClaudePty(),
                new ClaudeModelCatalog());
            runner.Register(
                new CodexCli(),
                new CodexEventParser(),
                new CodexHealthCheck(),
                new CodexFailureAnalyzer(),
                new CodexSessionCostParser(),
                new CodexPty(),
                new CodexModelCatalog());
            runner.Register(
                new CopilotCli(),
                new CopilotEventParser(),
                new CopilotHealthCheck(),
                new CopilotFailureAnalyzer(),
                new CopilotSessionCostParser(),
                new CopilotPty(),
                new CopilotModelCatalog());
            runner.Register(
                new GeminiCli(),
                new GeminiEventParser(),
                new GeminiHealthCheck(),
                new GeminiFailureAnalyzer(),
                new GeminiSessionCostParser(),
                new GeminiPty(),
                new GeminiModelCatalog());
            runner.Register(
                new OpenCodeCli(),
                new OpenCodeEventParser(),
                new OpenCodeHealthCheck(),
                new OpenCodeFailureAnalyzer(),
                new OpenCodeSessionCostParser(),
                new OpenCodePty(),
                new OpenCodeModelCatalog());

            if (options.IncludeBetaProviders)
            {
                Func<string?> apiKeyProvider = options.IvyApiKeyProviderFactory?.Invoke(sp) ?? (() => null);
                Func<string?> tokenProvider = options.IvyTokenProviderFactory?.Invoke(sp) ?? (() => null);
                Func<CancellationToken, Task<string?>> emailProvider = options.IvyEmailProviderFactory?.Invoke(sp) ?? ((_) => Task.FromResult<string?>(null));

                runner.Register(
                    new Providers.Ivy.IvyCli(apiKeyProvider),
                    new Providers.Ivy.IvyEventParser(),
                    new Providers.Ivy.IvyHealthCheck(apiKeyProvider, tokenProvider, emailProvider),
                    new Providers.Ivy.IvyFailureAnalyzer(),
                    new Providers.Ivy.IvySessionCostParser(),
                    new Providers.Ivy.IvyPty(apiKeyProvider),
                    new Providers.Ivy.IvyModelCatalog());

                Func<string?> openAiProxyApiKeyProvider = options.OpenAiProxyApiKeyProviderFactory?.Invoke(sp) ?? (() => null);
                Func<string?> openAiProxyBaseUrlProvider = options.OpenAiProxyBaseUrlProviderFactory?.Invoke(sp) ?? (() => null);

                runner.Register(
                    new Providers.OpenAiProxy.OpenAiProxyCli(openAiProxyApiKeyProvider, openAiProxyBaseUrlProvider),
                    new Providers.OpenAiProxy.OpenAiProxyEventParser(),
                    new Providers.OpenAiProxy.OpenAiProxyHealthCheck(openAiProxyApiKeyProvider, openAiProxyBaseUrlProvider),
                    new Providers.OpenAiProxy.OpenAiProxyFailureAnalyzer(),
                    new Providers.OpenAiProxy.OpenAiProxySessionCostParser(),
                    new Providers.OpenAiProxy.OpenAiProxyPty(openAiProxyApiKeyProvider, openAiProxyBaseUrlProvider),
                    new Providers.OpenAiProxy.OpenAiProxyModelCatalog());
            }

            return runner;
        });

        services.AddSingleton<IModelPricingProvider>(sp =>
        {
            var runner = sp.GetRequiredService<IAgentRunner>();
            var provider = new ModelPricingProvider(runner.ModelCatalogs);
            provider.AddPricing(options.AdditionalPricing);
            return provider;
        });

        return services;
    }
}
