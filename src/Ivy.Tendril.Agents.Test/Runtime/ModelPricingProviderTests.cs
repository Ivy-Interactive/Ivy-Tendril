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
        Assert.Equal(5m, pricing.InputPerMillion);
        Assert.Equal(25m, pricing.OutputPerMillion);
    }

    [Fact]
    public void GetPricing_Sonnet_ReturnsPricing()
    {
        var pricing = _provider.GetPricing("sonnet");

        Assert.NotNull(pricing);
        Assert.Equal(2m, pricing.InputPerMillion);
        Assert.Equal(10m, pricing.OutputPerMillion);
    }

    [Fact]
    public void GetPricing_Sonnet4_SubstringMatch_ReturnsPricing()
    {
        var pricing = _provider.GetPricing("claude-sonnet-4");

        Assert.NotNull(pricing);
        Assert.Equal(2m, pricing.InputPerMillion);
        Assert.Equal(10m, pricing.OutputPerMillion);
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

        Assert.Equal(5m + 25m, cost);
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

        Assert.Equal(0.50m + 6.25m, cost);
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

        var expected = (1000m * 2m / 1_000_000m) + (500m * 10m / 1_000_000m);
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

        Assert.Equal(6.25m, pricing.CacheWritePerMillion);
        Assert.Equal(0.50m, pricing.CacheReadPerMillion);
    }

    [Fact]
    public void GetPricing_SubstringMatch_FullModelIdFromOutput()
    {
        var pricing = _provider.GetPricing("claude-opus-4-7-20250219");

        Assert.NotNull(pricing);
        Assert.Contains("opus", pricing.Model);
    }

    [Fact]
    public void GetPricing_StaticCatalog_IsLabelledAsSuch()
    {
        // The cost sheet shows this label, so it has to say where the rates really came from until
        // RefreshFromModelsDevAsync has run.
        Assert.Equal("Static catalog (claude)", _provider.GetPricing("opus")!.Source);
    }

    // The models.dev warm-up writes through AddPricing on a background thread. Fetching is a live
    // HTTP call and is not exercised here; this covers what the refresh does to the provider.
    [Fact]
    public void AddPricing_OverwritesRatesAndRelabelsTheSource()
    {
        var provider = new ModelPricingProvider();
        var refreshed = provider.GetPricing("opus")! with
        {
            InputPerMillion = 7m,
            OutputPerMillion = 35m,
            Source = ModelsDevPricingSource.SourceUrl,
        };

        provider.AddPricing([refreshed]);

        var pricing = provider.GetPricing("opus")!;
        Assert.Equal(7m, pricing.InputPerMillion);
        Assert.Equal(35m, pricing.OutputPerMillion);
        Assert.Equal(ModelsDevPricingSource.SourceUrl, pricing.Source);
        // The refreshed rates are what cost math must now use.
        Assert.Equal(7m, provider.CalculateCost("opus", inputTokens: 1_000_000, outputTokens: 0));
    }

    [Fact]
    public void AddPricing_Empty_LeavesTheTableIntact()
    {
        var provider = new ModelPricingProvider();

        provider.AddPricing([]);

        Assert.NotNull(provider.GetPricing("opus"));
        Assert.NotNull(provider.GetPricing("claude-opus-4-7-20250219"));
    }

    [Fact]
    public void AddPricing_NewModel_IsFoundByTheSubstringFallback()
    {
        var provider = new ModelPricingProvider();

        provider.AddPricing([
            new ModelPricing { Model = "brand-new-model", InputPerMillion = 1m, OutputPerMillion = 2m }
        ]);

        // Proves the key index was rebuilt, not just the dictionary.
        Assert.Equal("brand-new-model", provider.GetPricing("vendor/brand-new-model-20260101")?.Model);
    }
}
