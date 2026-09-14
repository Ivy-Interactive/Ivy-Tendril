using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Runtime;

namespace Ivy.Tendril.Agents.Test.Runtime;

public class ModelSpecsTests
{
    [Fact]
    public void Find_ExactId_ReturnsSpec()
    {
        var spec = ModelSpecs.Find("claude-opus-5");

        Assert.NotNull(spec);
        Assert.Equal(1_000_000, spec.ContextWindow);
        Assert.Equal(128_000, spec.MaxOutputTokens);
        Assert.Equal(5.00m, spec.InputPerMillion);
        Assert.Equal(25.00m, spec.OutputPerMillion);
        Assert.Equal(0.50m, spec.CacheReadPerMillion);
        Assert.Equal(6.25m, spec.CacheWritePerMillion);
    }

    [Fact]
    public void Find_IsCaseInsensitive()
    {
        Assert.Same(ModelSpecs.Find("claude-opus-5"), ModelSpecs.Find("CLAUDE-OPUS-5"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    [InlineData("llama-3-70b")]
    [InlineData("mistral-large")]
    public void Find_UnknownId_ReturnsNull(string? modelId)
    {
        Assert.Null(ModelSpecs.Find(modelId));
    }

    [Theory]
    [InlineData("anthropic/claude-opus-5")]
    [InlineData("eu.anthropic.claude-opus-5")]
    [InlineData("global.anthropic.claude-opus-5")]
    [InlineData("us.anthropic.claude-opus-5")]
    [InlineData("bedrock/anthropic.claude-opus-5")]
    public void Find_StripsProviderPrefix(string formattedModel)
    {
        Assert.Same(ModelSpecs.Find("claude-opus-5"), ModelSpecs.Find(formattedModel));
    }

    [Theory]
    [InlineData("claude-3-5-sonnet", "claude-3.5-sonnet")]
    [InlineData("claude-3-7-sonnet", "claude-3.7-sonnet")]
    [InlineData("claude-opus-5-1", "claude-opus-5.1")]
    [InlineData("claude-sonnet-5-1", "claude-sonnet-5.1")]
    public void Find_DotAndDashSpellings_ResolveSameSpec(string dashed, string dotted)
    {
        var spec = ModelSpecs.Find(dashed);

        Assert.NotNull(spec);
        Assert.Same(spec, ModelSpecs.Find(dotted));
    }

    [Fact]
    public void Find_LegacyClaude3xSonnet_Is200KWindow()
    {
        // The catalogs claimed 1M/128k for models that publish 200k/64k, and nothing read the value
        // so nothing caught it.
        var spec = ModelSpecs.Find("claude-3-5-sonnet");

        Assert.NotNull(spec);
        Assert.Equal(200_000, spec.ContextWindow);
        Assert.Equal(64_000, spec.MaxOutputTokens);
    }

    [Fact]
    public void Find_PrefersLongestPattern()
    {
        // The old table scanned in declaration order, so a dated `claude-opus-4-8` was captured by
        // the `claude-opus-4` row and priced at 15/75 — three times its real rate — with a 200k
        // window instead of 1M.
        var spec = ModelSpecs.Find("anthropic/claude-opus-4-8-20260101");

        Assert.NotNull(spec);
        Assert.Equal(1_000_000, spec.ContextWindow);
        Assert.Equal(5.00m, spec.InputPerMillion);
        Assert.Equal(25.00m, spec.OutputPerMillion);
    }

    [Theory]
    [InlineData("gpt-5.4-mini-2026-01-01", 16_000)]
    [InlineData("gpt-4.1-nano-preview", 16_000)]
    [InlineData("gpt-4.1-2025-04-14", 32_000)]
    public void Find_PrefersLongestPattern_ForOpenAiSuffixes(string modelId, int expectedOutput)
    {
        var spec = ModelSpecs.Find(modelId);

        Assert.NotNull(spec);
        Assert.Equal(expectedOutput, spec.MaxOutputTokens);
    }

    [Fact]
    public void Find_ClaudeAliases_MatchTheModelTheyResolveTo()
    {
        // Separate rows, so value equality rather than reference equality: an alias is its own key
        // whose numbers must track the model it stands for.
        Assert.Equal(ModelSpecs.Find("claude-opus-5"), ModelSpecs.Find("opus"));
        Assert.Equal(ModelSpecs.Find("claude-sonnet-5"), ModelSpecs.Find("sonnet"));
        Assert.Equal(ModelSpecs.Find("claude-haiku-5-1"), ModelSpecs.Find("haiku"));
    }

    [Fact]
    public void Require_DeclaredId_ReturnsSpec()
    {
        // Declared with the provider prefix stripped, which is how the catalog spells it.
        var spec = ModelSpecs.Require("moonshotai/Kimi-K3");

        Assert.Equal(327_680, spec.ContextWindow);
        Assert.Equal(32_768, spec.MaxOutputTokens);
    }

    [Fact]
    public void Require_UnknownId_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ModelSpecs.Require("llama-3-70b"));
        Assert.Contains("llama-3-70b", ex.Message);
    }

    [Fact]
    public void Require_DoesNotFallBackToSubstringMatch()
    {
        // A catalog entry nobody has priced must break loudly rather than inherit a shorter row's
        // numbers, even though the same ID resolves through Find's substring fallback.
        Assert.NotNull(ModelSpecs.Find("claude-opus-4-8-20260101"));
        Assert.Throws<InvalidOperationException>(() => ModelSpecs.Require("claude-opus-4-8-20260101"));
    }

    [Fact]
    public void WithSpec_FillsEveryNumericField()
    {
        var model = new ModelInfo { Id = "claude-sonnet-5", DisplayName = "Claude Sonnet 5" }.WithSpec();

        Assert.Equal(1_000_000, model.ContextWindow);
        Assert.Equal(128_000, model.MaxOutputTokens);
        Assert.Equal(3.00m, model.InputPerMillion);
        Assert.Equal(15.00m, model.OutputPerMillion);
        Assert.Equal(0.30m, model.CacheReadPerMillion);
        Assert.Equal(3.75m, model.CacheWritePerMillion);
    }

    [Fact]
    public void WithSpec_AppliesProviderOutputCap()
    {
        var model = new ModelInfo { Id = "claude-sonnet-5", DisplayName = "Claude Sonnet 5" }.WithSpec(65_536);

        Assert.Equal(65_536, model.MaxOutputTokens);
        Assert.Equal(1_000_000, model.ContextWindow);
    }

    [Fact]
    public void WithSpec_RejectsOutputCapAboveCanonical()
    {
        var model = new ModelInfo { Id = "claude-haiku-5-1", DisplayName = "Claude Haiku 5.1" };

        Assert.Throws<ArgumentOutOfRangeException>(() => model.WithSpec(128_000));
    }

    [Fact]
    public void WithSpec_UnknownId_Throws()
    {
        var model = new ModelInfo { Id = "llama-3-70b", DisplayName = "Llama 3 70B" };

        Assert.Throws<InvalidOperationException>(() => model.WithSpec());
    }
}
