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
    public string? RecordingBasePath { get; set; }
    public ConcurrencyOptions? Concurrency { get; set; }
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

        services.AddSingleton<IModelPricingProvider>(
            new ModelPricingProvider(options.AdditionalPricing));

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
                new AntigravityPty());
            runner.Register(
                new ClaudeCli(),
                new ClaudeEventParser(),
                new ClaudeHealthCheck(),
                new ClaudeFailureAnalyzer(),
                new ClaudeSessionCostParser(),
                new ClaudePty());
            runner.Register(
                new CodexCli(),
                new CodexEventParser(),
                new CodexHealthCheck(),
                new CodexFailureAnalyzer(),
                new CodexSessionCostParser(),
                new CodexPty());
            runner.Register(
                new CopilotCli(),
                new CopilotEventParser(),
                new CopilotHealthCheck(),
                new CopilotFailureAnalyzer(),
                new CopilotSessionCostParser(),
                new CopilotPty());
            runner.Register(
                new GeminiCli(),
                new GeminiEventParser(),
                new GeminiHealthCheck(),
                new GeminiFailureAnalyzer(),
                new GeminiSessionCostParser(),
                new GeminiPty());
            runner.Register(
                new OpenCodeCli(),
                new OpenCodeEventParser(),
                new OpenCodeHealthCheck(),
                new OpenCodeFailureAnalyzer(),
                new OpenCodeSessionCostParser(),
                new OpenCodePty());
            return runner;
        });

        return services;
    }

    public static IServiceCollection AddAgent<TCli, TParser, THealthCheck>(
        this IServiceCollection services)
        where TCli : class, IAgentCli
        where TParser : class, IEventParser
        where THealthCheck : class, IAgentHealthCheck
    {
        services.AddSingleton<TCli>();
        services.AddTransient<TParser>();
        services.AddSingleton<THealthCheck>();
        return services;
    }
}
