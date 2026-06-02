using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Runtime;

namespace Ivy.Tendril.Agents.Test.Runtime;

public class ModelPricingProviderTests
{
    private readonly ModelPricingProvider _provider = new();

    [Fact]
    public void GetPricing_KnownModel_ReturnsPricing()
    {
        var pricing = _provider.GetPricing("opus");

        Assert.NotNull(pricing);
        Assert.Equal("opus", pricing.Model);
        Assert.Equal(10m, pricing.InputPerMillion);
        Assert.Equal(50m, pricing.OutputPerMillion);
    }

    [Fact]
    public void GetPricing_Sonnet_ReturnsPricing()
    {
        var pricing = _provider.GetPricing("sonnet");

        Assert.NotNull(pricing);
        Assert.Equal(3m, pricing.InputPerMillion);
        Assert.Equal(15m, pricing.OutputPerMillion);
    }

    [Fact]
    public void GetPricing_Sonnet4_SubstringMatch_ReturnsPricing()
    {
        var pricing = _provider.GetPricing("claude-sonnet-4");

        Assert.NotNull(pricing);
        Assert.Equal(3m, pricing.InputPerMillion);
        Assert.Equal(15m, pricing.OutputPerMillion);
    }

    [Fact]
    public void GetPricing_Haiku_ReturnsPricing()
    {
        var pricing = _provider.GetPricing("haiku");

        Assert.NotNull(pricing);
        Assert.Equal(1m, pricing.InputPerMillion);
        Assert.Equal(5m, pricing.OutputPerMillion);
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
        var pricing = _provider.GetPricing("some-prefix-claude-opus-4-suffix");

        Assert.NotNull(pricing);
        Assert.Contains("opus", pricing.Model);
    }

    [Fact]
    public void GetPricing_CaseInsensitive()
    {
        var pricing = _provider.GetPricing("Opus");

        Assert.NotNull(pricing);
    }

    [Fact]
    public void CalculateCost_KnownModel_ReturnsCorrectCost()
    {
        var cost = _provider.CalculateCost(
            "opus",
            inputTokens: 1_000_000,
            outputTokens: 1_000_000);

        Assert.Equal(10m + 50m, cost);
    }

    [Fact]
    public void CalculateCost_WithCache_IncludesCacheCost()
    {
        var cost = _provider.CalculateCost(
            "opus",
            inputTokens: 0,
            outputTokens: 0,
            cacheReadTokens: 1_000_000,
            cacheWriteTokens: 1_000_000);

        Assert.Equal(1.00m + 12.50m, cost);
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
            "sonnet",
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
            Model = "opus",
            InputPerMillion = 99m,
            OutputPerMillion = 99m,
        };

        var provider = new ModelPricingProvider([override_]);
        var pricing = provider.GetPricing("opus");

        Assert.NotNull(pricing);
        Assert.Equal(99m, pricing.InputPerMillion);
    }

    [Fact]
    public void Constructor_Default_IncludesAllKnownModels()
    {
        Assert.NotNull(_provider.GetPricing("opus"));
        Assert.NotNull(_provider.GetPricing("sonnet"));
        Assert.NotNull(_provider.GetPricing("haiku"));
    }

    [Fact]
    public void GetPricing_IncludesCacheRates()
    {
        var pricing = _provider.GetPricing("opus")!;

        Assert.Equal(12.50m, pricing.CacheWritePerMillion);
        Assert.Equal(1.00m, pricing.CacheReadPerMillion);
    }

    [Fact]
    public void GetPricing_SubstringMatch_FullModelIdFromOutput()
    {
        var pricing = _provider.GetPricing("claude-opus-4-7-20250219");

        Assert.NotNull(pricing);
        Assert.Contains("opus", pricing.Model);
    }
}
