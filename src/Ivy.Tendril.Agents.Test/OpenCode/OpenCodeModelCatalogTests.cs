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
        Assert.NotEmpty(models);
        Assert.Contains(models, m => m.Id == "moonshotai/Kimi-K3" && m.IsDefault);
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
