using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.OpenCode;

namespace Ivy.Tendril.Agents.Test.OpenCode;

public class OpenCodeModelCatalogTests
{
    private readonly OpenCodeModelCatalog _catalog = new();

    [Fact]
    public void AgentId_IsOpenCode()
    {
        Assert.Equal("opencode", _catalog.AgentId);
    }

    [Fact]
    public void GetStaticModels_ReturnsDefault()
    {
        var models = _catalog.GetStaticModels();
        Assert.Single(models);
        Assert.True(models[0].IsDefault);
        Assert.Equal("default", models[0].Id);
    }

    [Fact]
    public async Task GetModelsAsync_WhenDiscoveryFails_FallsBackToStatic()
    {
        var result = await _catalog.GetModelsAsync();

        Assert.NotNull(result);
        Assert.Equal("opencode", result.AgentId);
        Assert.NotEmpty(result.Models);
    }
}
