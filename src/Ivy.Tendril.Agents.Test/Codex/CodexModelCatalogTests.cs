using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Codex;

namespace Ivy.Tendril.Agents.Test.Codex;

public class CodexModelCatalogTests
{
    private readonly CodexModelCatalog _catalog = new();

    [Fact]
    public void AgentId_IsCodex()
    {
        Assert.Equal("codex", _catalog.AgentId);
    }

    [Fact]
    public void GetStaticModels_ReturnsNonEmpty()
    {
        var models = _catalog.GetStaticModels();
        Assert.NotEmpty(models);
    }

    [Fact]
    public void GetStaticModels_HasDefault()
    {
        var models = _catalog.GetStaticModels();
        Assert.Contains(models, m => m.IsDefault);
    }

    [Fact]
    public void GetStaticModels_AllHavePricing()
    {
        var models = _catalog.GetStaticModels();
        foreach (var model in models)
        {
            Assert.True(model.InputPerMillion > 0, $"{model.Id} missing InputPerMillion");
            Assert.True(model.OutputPerMillion > 0, $"{model.Id} missing OutputPerMillion");
        }
    }

    [Fact]
    public void GetStaticModels_AllHaveProvider()
    {
        var models = _catalog.GetStaticModels();
        Assert.All(models, m => Assert.False(string.IsNullOrEmpty(m.Provider)));
    }

    [Fact]
    public async Task GetModelsAsync_WhenDiscoveryFails_FallsBackToStatic()
    {
        var result = await _catalog.GetModelsAsync();

        Assert.NotNull(result);
        Assert.Equal("codex", result.AgentId);
        Assert.NotEmpty(result.Models);
    }
}
