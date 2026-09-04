using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Antigravity;

namespace Ivy.Tendril.Agents.Test.Antigravity;

public class AntigravityModelCatalogTests
{
    private readonly AntigravityModelCatalog _catalog = new();

    [Fact]
    public void AgentId_IsAntigravity()
    {
        Assert.Equal(AgentId.Antigravity, _catalog.AgentId);
    }

    [Fact]
    public void GetStaticModels_ReturnsNonEmpty()
    {
        var models = _catalog.GetStaticModels();
        Assert.NotEmpty(models);
    }

    [Fact]
    public void GetStaticModels_HasExactlyOneDefault()
    {
        var models = _catalog.GetStaticModels();
        Assert.Single(models, m => m.IsDefault);
    }

    [Fact]
    public void GetStaticModels_AllHaveProvider()
    {
        var models = _catalog.GetStaticModels();
        Assert.All(models, m => Assert.False(string.IsNullOrEmpty(m.Provider)));
    }

    [Fact]
    public void GetStaticModels_IdsAreUnique()
    {
        var ids = _catalog.GetStaticModels().Select(m => m.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Theory]
    [InlineData("gemini-3.8-flash")]
    [InlineData("gemini-3.7-flash")]
    [InlineData("gemini-3.6-flash")]
    [InlineData("gemini-3.1-pro")]
    [InlineData("claude-fable-5")]
    [InlineData("claude-opus-5-1")]
    [InlineData("claude-opus-5")]
    [InlineData("claude-sonnet-5-1")]
    [InlineData("claude-5.1")]
    [InlineData("claude-sonnet-5")]
    [InlineData("claude-sonnet-4-6")]
    public void GetStaticModels_ContainsModels(string expectedId)
    {
        var models = _catalog.GetStaticModels();
        Assert.Contains(models, m => m.Id == expectedId);
    }

    [Fact]
    public void GetStaticModels_ContainsGemini38Flash()
    {
        var models = _catalog.GetStaticModels();
        var flash = models.FirstOrDefault(m => m.Id == "gemini-3.8-flash");
        Assert.NotNull(flash);
        Assert.Equal("Gemini 3.8 Flash", flash!.DisplayName);
        Assert.Equal("google", flash.Provider);
        Assert.Equal(0.15m, flash.InputPerMillion);
        Assert.Equal(0.60m, flash.OutputPerMillion);
    }

    [Fact]
    public void GetStaticModels_DefaultIsGemini37Flash()
    {
        var defaultModel = _catalog.GetStaticModels().Single(m => m.IsDefault);
        Assert.Equal("gemini-3.7-flash", defaultModel.Id);
    }

    [Fact]
    public void GetStaticModels_AllHavePricingRates()
    {
        var models = _catalog.GetStaticModels();
        Assert.All(models, m =>
        {
            Assert.True(m.InputPerMillion > 0, $"{m.Id} should have InputPerMillion > 0");
            Assert.True(m.OutputPerMillion > 0, $"{m.Id} should have OutputPerMillion > 0");
        });
    }

    [Fact]
    public void ModelPricingProvider_ResolvesAntigravityModels()
    {
        var provider = new Ivy.Tendril.Agents.Runtime.ModelPricingProvider([_catalog]);
        var pricing = provider.GetPricing("gemini-3.7-flash");

        Assert.NotNull(pricing);
        Assert.Equal(0.15m, pricing.InputPerMillion);
        Assert.Equal(0.60m, pricing.OutputPerMillion);

        var cost = provider.CalculateCost("gemini-3.7-flash", 1_000_000, 1_000_000);
        Assert.Equal(0.75m, cost);
    }
}
