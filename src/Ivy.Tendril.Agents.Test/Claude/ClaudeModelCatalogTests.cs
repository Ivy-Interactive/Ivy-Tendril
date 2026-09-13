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
    public void GetStaticModels_ContainsFable5()
    {
        var models = _catalog.GetStaticModels();
        var fable5 = Assert.Single(models, m => m.Id == "claude-fable-5");

        Assert.Equal(1_000_000, fable5.ContextWindow);
        Assert.Equal(128_000, fable5.MaxOutputTokens);
        Assert.Equal(10.00m, fable5.InputPerMillion);
        Assert.Equal(50.00m, fable5.OutputPerMillion);
        Assert.Equal(12.50m, fable5.CacheWritePerMillion);
        Assert.Equal(1.00m, fable5.CacheReadPerMillion);
    }

    [Fact]
    public void GetStaticModels_ContainsOpus51()
    {
        var models = _catalog.GetStaticModels();
        var opus51 = Assert.Single(models, m => m.Id == "claude-opus-5-1");

        Assert.Equal(1_000_000, opus51.ContextWindow);
        Assert.Equal(128_000, opus51.MaxOutputTokens);
        Assert.Equal(5.00m, opus51.InputPerMillion);
        Assert.Equal(25.00m, opus51.OutputPerMillion);
        Assert.Equal(6.25m, opus51.CacheWritePerMillion);
        Assert.Equal(0.50m, opus51.CacheReadPerMillion);
    }

    [Fact]
    public void GetStaticModels_ContainsSonnet51()
    {
        var models = _catalog.GetStaticModels();
        var sonnet51 = Assert.Single(models, m => m.Id == "claude-sonnet-5-1");

        Assert.Equal(1_000_000, sonnet51.ContextWindow);
        Assert.Equal(128_000, sonnet51.MaxOutputTokens);
        Assert.Equal(3.00m, sonnet51.InputPerMillion);
        Assert.Equal(15.00m, sonnet51.OutputPerMillion);
        Assert.Equal(3.75m, sonnet51.CacheWritePerMillion);
        Assert.Equal(0.30m, sonnet51.CacheReadPerMillion);
    }

    [Fact]
    public void GetStaticModels_ContainsClaude51()
    {
        var models = _catalog.GetStaticModels();
        var claude51 = Assert.Single(models, m => m.Id == "claude-5.1");

        Assert.Equal(1_000_000, claude51.ContextWindow);
        Assert.Equal(128_000, claude51.MaxOutputTokens);
        Assert.Equal(3.00m, claude51.InputPerMillion);
        Assert.Equal(15.00m, claude51.OutputPerMillion);
        Assert.Equal(3.75m, claude51.CacheWritePerMillion);
        Assert.Equal(0.30m, claude51.CacheReadPerMillion);
    }

    [Fact]
    public void GetStaticModels_ContainsHaiku51()
    {
        var models = _catalog.GetStaticModels();
        var haiku51 = Assert.Single(models, m => m.Id == "claude-haiku-5-1");

        Assert.Equal(200_000, haiku51.ContextWindow);
        Assert.Equal(64_000, haiku51.MaxOutputTokens);
        Assert.Equal(1.00m, haiku51.InputPerMillion);
        Assert.Equal(5.00m, haiku51.OutputPerMillion);
        Assert.Equal(1.25m, haiku51.CacheWritePerMillion);
        Assert.Equal(0.10m, haiku51.CacheReadPerMillion);
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
