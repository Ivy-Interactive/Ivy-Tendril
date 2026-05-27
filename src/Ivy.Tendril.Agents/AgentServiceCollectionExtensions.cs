using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Antigravity;
using Ivy.Tendril.Agents.Providers.Claude;
using Ivy.Tendril.Agents.Providers.Codex;
using Ivy.Tendril.Agents.Providers.Copilot;
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
                new OpenCodeCli(),
                new OpenCodeEventParser(),
                new OpenCodeHealthCheck(),
                new OpenCodeFailureAnalyzer(),
                new OpenCodeSessionCostParser(),
                new OpenCodePty(),
                new OpenCodeModelCatalog());
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
