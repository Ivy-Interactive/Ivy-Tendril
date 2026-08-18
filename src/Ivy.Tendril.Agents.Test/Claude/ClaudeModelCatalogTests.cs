using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Claude;

namespace Ivy.Tendril.Agents.Test.Claude;

public class ClaudeModelCatalogTests
{
    private readonly ClaudeModelCatalog _catalog = new();

    [Fact]
    public void AgentId_IsClaude()
    {
        Assert.Equal("claude", _catalog.AgentId);
    }

    [Fact]
    public void GetStaticModels_HasExactlyOneDefault()
    {
        var models = _catalog.GetStaticModels();
        Assert.Single(models, m => m.IsDefault);
    }

    [Fact]
    public void GetStaticModels_IdsAreUnique()
    {
        var ids = _catalog.GetStaticModels().Select(m => m.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
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
    public void GetStaticModels_ContainsOpus5()
    {
        var models = _catalog.GetStaticModels();
        var opus5 = Assert.Single(models, m => m.Id == "claude-opus-5");

        Assert.Equal(1_000_000, opus5.ContextWindow);
        Assert.Equal(128_000, opus5.MaxOutputTokens);
        Assert.Equal(5.00m, opus5.InputPerMillion);
        Assert.Equal(25.00m, opus5.OutputPerMillion);
    }

    [Fact]
    public void GetStaticModels_ContainsSonnet5()
    {
        var models = _catalog.GetStaticModels();
        var sonnet5 = Assert.Single(models, m => m.Id == "claude-sonnet-5");

        Assert.Equal(1_000_000, sonnet5.ContextWindow);
        Assert.Equal(128_000, sonnet5.MaxOutputTokens);
        Assert.Equal(3.00m, sonnet5.InputPerMillion);
        Assert.Equal(15.00m, sonnet5.OutputPerMillion);
    }

    [Fact]
    public async Task GetModelsAsync_ReturnsModels()
    {
        var result = await _catalog.GetModelsAsync();

        Assert.NotNull(result);
        Assert.Equal("claude", result.AgentId);
        Assert.NotEmpty(result.Models);
    }
}
