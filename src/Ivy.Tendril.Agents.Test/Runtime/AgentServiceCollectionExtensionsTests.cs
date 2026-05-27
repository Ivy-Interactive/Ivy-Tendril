using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Ivy.Tendril.Agents.Test.Runtime;

public class AgentServiceCollectionExtensionsTests
{
    [Fact]
    public void AddAgentInfrastructure_RegistersRunner()
    {
        var services = new ServiceCollection();
        services.AddAgentInfrastructure();
        var sp = services.BuildServiceProvider();

        var runner = sp.GetService<IAgentRunner>();

        Assert.NotNull(runner);
    }

    [Fact]
    public void AddAgentInfrastructure_RegistersClaude()
    {
        var services = new ServiceCollection();
        services.AddAgentInfrastructure();
        var sp = services.BuildServiceProvider();

        var runner = sp.GetRequiredService<IAgentRunner>();

        Assert.Contains(AgentId.Claude, runner.RegisteredAgents);
    }

    [Fact]
    public void AddAgentInfrastructure_RegistersEventSerializer()
    {
        var services = new ServiceCollection();
        services.AddAgentInfrastructure();
        var sp = services.BuildServiceProvider();

        var serializer = sp.GetService<IEventSerializer>();

        Assert.NotNull(serializer);
        Assert.IsType<JsonEventSerializer>(serializer);
    }

    [Fact]
    public void AddAgentInfrastructure_RegistersModelPricing()
    {
        var services = new ServiceCollection();
        services.AddAgentInfrastructure();
        var sp = services.BuildServiceProvider();

        var pricing = sp.GetService<IModelPricingProvider>();

        Assert.NotNull(pricing);
        Assert.IsType<ModelPricingProvider>(pricing);
    }

    [Fact]
    public void AddAgentInfrastructure_RegistersInteractionHandler_Default()
    {
        var services = new ServiceCollection();
        services.AddAgentInfrastructure();
        var sp = services.BuildServiceProvider();

        var handler = sp.GetService<IInteractionHandler>();

        Assert.NotNull(handler);
        Assert.Same(PassthroughHandler.Instance, handler);
    }

    [Fact]
    public void AddAgentInfrastructure_CustomInteractionHandler()
    {
        var services = new ServiceCollection();
        services.AddAgentInfrastructure(opts =>
        {
            opts.DefaultInteractionHandler = AutoApproveHandler.Instance;
        });
        var sp = services.BuildServiceProvider();

        var handler = sp.GetRequiredService<IInteractionHandler>();

        Assert.Same(AutoApproveHandler.Instance, handler);
    }

    [Fact]
    public void AddAgentInfrastructure_CustomPricing()
    {
        var custom = new ModelPricing
        {
            Model = "my-custom-model",
            InputPerMillion = 10m,
            OutputPerMillion = 20m,
        };

        var services = new ServiceCollection();
        services.AddAgentInfrastructure(opts =>
        {
            opts.AdditionalPricing = [custom];
        });
        var sp = services.BuildServiceProvider();

        var pricing = sp.GetRequiredService<IModelPricingProvider>();
        var result = pricing.GetPricing("my-custom-model");

        Assert.NotNull(result);
        Assert.Equal(10m, result.InputPerMillion);
    }

    [Fact]
    public void AddAgentInfrastructure_RunnerIsSingleton()
    {
        var services = new ServiceCollection();
        services.AddAgentInfrastructure();
        var sp = services.BuildServiceProvider();

        var runner1 = sp.GetRequiredService<IAgentRunner>();
        var runner2 = sp.GetRequiredService<IAgentRunner>();

        Assert.Same(runner1, runner2);
    }

    [Fact]
    public void AddAgentInfrastructure_RunnerHasClaudeHealthCheck()
    {
        var services = new ServiceCollection();
        services.AddAgentInfrastructure();
        var sp = services.BuildServiceProvider();

        var runner = sp.GetRequiredService<IAgentRunner>();
        var healthCheck = runner.GetHealthCheck(AgentId.Claude);

        Assert.NotNull(healthCheck);
    }
}
