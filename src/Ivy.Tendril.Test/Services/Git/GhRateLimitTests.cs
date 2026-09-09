using Ivy.Tendril.Services.Git;

namespace Ivy.Tendril.Test.Services.Git;

public class GhRateLimitTests
{
    [Theory]
    [InlineData("HTTP 403: You have exceeded a secondary rate limit. Please wait a few minutes before you try again.")]
    [InlineData("API rate limit exceeded for user ID 12345.")]
    [InlineData("You have triggered an abuse detection mechanism.")]
    [InlineData("was submitted too quickly")]
    [InlineData("Retry-After: 60")]
    public void IsRateLimitError_ReturnsTrue_ForRateLimitErrors(string stderr)
    {
        Assert.True(GhRateLimit.IsRateLimitError(stderr));
    }

    [Theory]
    [InlineData("GraphQL: Could not resolve to a Repository")]
    [InlineData("fatal: not a git repository")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsRateLimitError_ReturnsFalse_ForNonRateLimitErrors(string? stderr)
    {
        Assert.False(GhRateLimit.IsRateLimitError(stderr));
    }

    [Fact]
    public void ParseRetryAfter_ExtractsHeader_AndClampsToMax()
    {
        var result42 = GhRateLimit.ParseRetryAfter("HTTP 403: rate limited\nRetry-After: 42\nDetails...");
        Assert.NotNull(result42);
        Assert.Equal(TimeSpan.FromSeconds(42), result42.Value);

        var resultClamped = GhRateLimit.ParseRetryAfter("HTTP 403: rate limited\nRetry-After: 9999");
        Assert.NotNull(resultClamped);
        Assert.Equal(TimeSpan.FromSeconds(120), resultClamped.Value);

        var resultAbsent = GhRateLimit.ParseRetryAfter("HTTP 403: You have exceeded a secondary rate limit.");
        Assert.Null(resultAbsent);

        var resultNull = GhRateLimit.ParseRetryAfter(null);
        Assert.Null(resultNull);
    }

    [Fact]
    public void GetRetryDelay_HonoursSuppliedRetryAfter_Verbatim()
    {
        var customDelay = TimeSpan.FromSeconds(42);
        var delay = GhRateLimit.GetRetryDelay(1, customDelay);
        Assert.Equal(customDelay, delay);
    }

    [Fact]
    public void GetRetryDelay_ReturnsIncreasingDelaysWithinBasePlusJitterRange()
    {
        for (var i = 0; i < 20; i++)
        {
            var delay1 = GhRateLimit.GetRetryDelay(1);
            Assert.InRange(delay1.TotalSeconds, 2.0, 2.41);

            var delay2 = GhRateLimit.GetRetryDelay(2);
            Assert.InRange(delay2.TotalSeconds, 8.0, 9.61);

            var delay3 = GhRateLimit.GetRetryDelay(3);
            Assert.InRange(delay3.TotalSeconds, 30.0, 36.01);

            Assert.True(delay2 > delay1);
            Assert.True(delay3 > delay2);
        }
    }
}
