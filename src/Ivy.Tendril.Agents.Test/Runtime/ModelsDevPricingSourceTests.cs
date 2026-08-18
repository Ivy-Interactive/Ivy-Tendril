using Ivy.Tendril.Agents.Runtime;

namespace Ivy.Tendril.Agents.Test.Runtime;

/// <summary>
/// Covers the cache lookup only. Fetching is a live HTTP call to models.dev and is deliberately not
/// exercised here; <see cref="ModelsDevPricingSource.GetCacheAsync" /> swallows its own failures and
/// returns the previous (or no) cache.
/// </summary>
public class ModelsDevPricingSourceTests
{
    private static ModelsDevPricingSource.ModelPricingEntry Entry(decimal input) =>
        new(input, input * 5, 0.1m, 1m, ContextWindow: 200_000, MaxOutputTokens: 64_000);

    private static Dictionary<string, ModelsDevPricingSource.ModelPricingEntry> Cache() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["claude-opus-5"] = Entry(5m),
            ["gpt-5-codex"] = Entry(1.25m),
        };

    [Fact]
    public void Find_ExactId_ReturnsEntry()
    {
        Assert.Equal(5m, ModelsDevPricingSource.Find(Cache(), "claude-opus-5")!.InputPerMillion);
    }

    [Fact]
    public void Find_ProviderPrefixedId_StripsThePrefix()
    {
        // Catalog ids are sometimes "provider/model" while models.dev keys the model alone.
        Assert.Equal(1.25m, ModelsDevPricingSource.Find(Cache(), "openai/gpt-5-codex")!.InputPerMillion);
    }

    [Fact]
    public void Find_SubstringFallback_MatchesEitherDirection()
    {
        Assert.Equal(5m, ModelsDevPricingSource.Find(Cache(), "opus-5")!.InputPerMillion);
        Assert.Equal(5m, ModelsDevPricingSource.Find(Cache(), "claude-opus-5-20260101")!.InputPerMillion);
    }

    [Fact]
    public void Find_NoMatch_ReturnsNull()
    {
        Assert.Null(ModelsDevPricingSource.Find(Cache(), "llama-4"));
    }
}
