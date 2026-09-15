using Ivy.Tendril.Agents.Runtime;

namespace Ivy.Tendril.Agents.Test.Runtime;

public class ProviderErrorClassifierTests
{
    [Theory]
    [InlineData("API error (attempt 5): RESOURCE_EXHAUSTED (code 429): Resource has been exhausted (e.g. check quota).")]
    [InlineData("code 429")]
    [InlineData("You have exceeded your quota for this model")]
    [InlineData("rate limit reached, try again later")]
    [InlineData("429 Too Many Requests")]
    public void Classify_QuotaText_ReturnsQuota(string text)
    {
        Assert.Equal(ProviderErrorClassifier.ProviderErrorKind.Quota, ProviderErrorClassifier.Classify(text));
    }

    [Theory]
    [InlineData("401 Unauthorized")]
    [InlineData("403 Forbidden")]
    [InlineData("request was unauthorized")]
    [InlineData("You are not logged in. Run `agy login` first.")]
    public void Classify_AuthText_ReturnsAuth(string text)
    {
        Assert.Equal(ProviderErrorClassifier.ProviderErrorKind.Auth, ProviderErrorClassifier.Classify(text));
    }

    [Theory]
    [InlineData("declaring permissions: cortex tool write_to_file: path is not valid")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Classify_OrdinaryOrEmptyText_ReturnsNone(string? text)
    {
        Assert.Equal(ProviderErrorClassifier.ProviderErrorKind.None, ProviderErrorClassifier.Classify(text));
    }

    // The symbolic name wins over the numeric one when a message carries both, because it says more.
    [Fact]
    public void ExtractCode_SymbolicAndNumeric_PrefersSymbolic()
    {
        var text = "API error (attempt 5): RESOURCE_EXHAUSTED (code 429): Resource has been exhausted (e.g. check quota).";

        Assert.Equal("RESOURCE_EXHAUSTED", ProviderErrorClassifier.ExtractCode(text));
    }

    [Fact]
    public void ExtractCode_NumericOnly_ReturnsNumber()
    {
        Assert.Equal("429", ProviderErrorClassifier.ExtractCode("HTTP 429 returned by the provider"));
        Assert.Equal("401", ProviderErrorClassifier.ExtractCode("401 Unauthorized"));
    }

    [Fact]
    public void ExtractCode_NoCode_ReturnsNull()
    {
        Assert.Null(ProviderErrorClassifier.ExtractCode("path is not valid"));
        Assert.Null(ProviderErrorClassifier.ExtractCode(null));
    }

    // A number that is not a status code must not be read as one — "attempt 5" and a token count are
    // both prose, and an unconstrained three-digit pattern would happily claim them.
    [Fact]
    public void ExtractCode_ProseNumbers_ReturnsNull()
    {
        Assert.Null(ProviderErrorClassifier.ExtractCode("read 512 lines from 137 files"));
    }

    [Fact]
    public void IsRetryable_QuotaOnly()
    {
        Assert.True(ProviderErrorClassifier.IsRetryable(ProviderErrorClassifier.ProviderErrorKind.Quota));
        Assert.False(ProviderErrorClassifier.IsRetryable(ProviderErrorClassifier.ProviderErrorKind.Auth));
        Assert.False(ProviderErrorClassifier.IsRetryable(ProviderErrorClassifier.ProviderErrorKind.None));
    }

    [Fact]
    public void Describe_NamesTheCondition()
    {
        Assert.Contains("quota", ProviderErrorClassifier.Describe(ProviderErrorClassifier.ProviderErrorKind.Quota));
        Assert.Contains("authentication", ProviderErrorClassifier.Describe(ProviderErrorClassifier.ProviderErrorKind.Auth));
        Assert.Equal("provider error", ProviderErrorClassifier.Describe(ProviderErrorClassifier.ProviderErrorKind.None));
    }
}
