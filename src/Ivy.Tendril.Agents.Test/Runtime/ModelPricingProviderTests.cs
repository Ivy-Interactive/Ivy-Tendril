using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Runtime;

namespace Ivy.Tendril.Agents.Test.Runtime;

public class ModelPricingProviderTests
{
    private readonly ModelPricingProvider _provider = new();

    [Fact]
    public void GetPricing_KnownModel_ReturnsPricing()
    {
        var pricing = _provider.GetPricing("claude-opus-4-20250514");

        Assert.NotNull(pricing);
        Assert.Equal("claude-opus-4-20250514", pricing.Model);
        Assert.Equal(15m, pricing.InputPerMillion);
        Assert.Equal(75m, pricing.OutputPerMillion);
    }

    [Fact]
    public void GetPricing_Sonnet_ReturnsPricing()
    {
        var pricing = _provider.GetPricing("claude-sonnet-4-5-20250514");

        Assert.NotNull(pricing);
        Assert.Equal(3m, pricing.InputPerMillion);
        Assert.Equal(15m, pricing.OutputPerMillion);
    }

    [Fact]
    public void GetPricing_Sonnet4_ReturnsPricing()
    {
        var pricing = _provider.GetPricing("claude-sonnet-4-20250514");

        Assert.NotNull(pricing);
        Assert.Equal(3m, pricing.InputPerMillion);
        Assert.Equal(15m, pricing.OutputPerMillion);
    }

    [Fact]
    public void GetPricing_Haiku_ReturnsPricing()
    {
        var pricing = _provider.GetPricing("claude-haiku-3-5-20241022");

        Assert.NotNull(pricing);
        Assert.Equal(0.80m, pricing.InputPerMillion);
        Assert.Equal(4m, pricing.OutputPerMillion);
    }

    [Fact]
    public void GetPricing_UnknownModel_ReturnsNull()
    {
        var pricing = _provider.GetPricing("gpt-4o");

        Assert.Null(pricing);
    }

    [Fact]
    public void GetPricing_PartialMatch_FindsPricing()
    {
        var pricing = _provider.GetPricing("some-prefix-claude-opus-4-20250514-suffix");

        Assert.NotNull(pricing);
        Assert.Contains("opus", pricing.Model);
    }

    [Fact]
    public void GetPricing_CaseInsensitive()
    {
        var pricing = _provider.GetPricing("Claude-Opus-4-20250514");

        Assert.NotNull(pricing);
    }

    [Fact]
    public void CalculateCost_KnownModel_ReturnsCorrectCost()
    {
        var cost = _provider.CalculateCost(
            "claude-opus-4-20250514",
            inputTokens: 1_000_000,
            outputTokens: 1_000_000);

        Assert.Equal(15m + 75m, cost);
    }

    [Fact]
    public void CalculateCost_WithCache_IncludesCacheCost()
    {
        var cost = _provider.CalculateCost(
            "claude-opus-4-20250514",
            inputTokens: 0,
            outputTokens: 0,
            cacheReadTokens: 1_000_000,
            cacheWriteTokens: 1_000_000);

        Assert.Equal(1.50m + 18.75m, cost);
    }

    [Fact]
    public void CalculateCost_UnknownModel_ReturnsZero()
    {
        var cost = _provider.CalculateCost("unknown-model", 1000, 500);

        Assert.Equal(0m, cost);
    }

    [Fact]
    public void CalculateCost_SmallUsage_ReturnsProportionalCost()
    {
        var cost = _provider.CalculateCost(
            "claude-sonnet-4-5-20250514",
            inputTokens: 1000,
            outputTokens: 500);

        var expected = (1000m * 3m / 1_000_000m) + (500m * 15m / 1_000_000m);
        Assert.Equal(expected, cost);
    }

    [Fact]
    public void Constructor_WithAdditionalPricing_IncludesCustomModel()
    {
        var custom = new ModelPricing
        {
            Model = "custom-model",
            InputPerMillion = 1m,
            OutputPerMillion = 2m,
        };

        var provider = new ModelPricingProvider([custom]);
        var pricing = provider.GetPricing("custom-model");

        Assert.NotNull(pricing);
        Assert.Equal(1m, pricing.InputPerMillion);
    }

    [Fact]
    public void Constructor_WithAdditionalPricing_OverridesExisting()
    {
        var override_ = new ModelPricing
        {
            Model = "claude-opus-4-20250514",
            InputPerMillion = 99m,
            OutputPerMillion = 99m,
        };

        var provider = new ModelPricingProvider([override_]);
        var pricing = provider.GetPricing("claude-opus-4-20250514");

        Assert.NotNull(pricing);
        Assert.Equal(99m, pricing.InputPerMillion);
    }

    [Fact]
    public void Constructor_Default_IncludesAllKnownModels()
    {
        Assert.NotNull(_provider.GetPricing("claude-opus-4-20250514"));
        Assert.NotNull(_provider.GetPricing("claude-sonnet-4-5-20250514"));
        Assert.NotNull(_provider.GetPricing("claude-sonnet-4-20250514"));
        Assert.NotNull(_provider.GetPricing("claude-haiku-3-5-20241022"));
    }

    [Fact]
    public void GetPricing_IncludesCacheRates()
    {
        var pricing = _provider.GetPricing("claude-opus-4-20250514")!;

        Assert.Equal(18.75m, pricing.CacheWritePerMillion);
        Assert.Equal(1.50m, pricing.CacheReadPerMillion);
    }
}
