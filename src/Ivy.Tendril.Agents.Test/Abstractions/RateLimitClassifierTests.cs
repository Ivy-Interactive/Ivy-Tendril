using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Test.Abstractions;

public class RateLimitClassifierTests
{
    [Fact]
    public void Classify_BedrockDailyTokenLimit_ReturnsDailyQuota()
    {
        // The exact message from issue #1756. It also contains "429", so the daily-quota
        // patterns have to win: the cooldown for a per-day quota is much longer.
        const string message =
            "API Error: Request rejected (429) - Too many tokens per day, please wait before trying again.";

        Assert.Equal(RateLimitScope.DailyQuota, RateLimitClassifier.Classify(message));
    }

    [Theory]
    [InlineData("daily token limit exceeded")]
    [InlineData("Your per-day token quota has been exceeded")]
    [InlineData("quota exceeded for this account")]
    public void Classify_DailyQuotaWording_ReturnsDailyQuota(string message)
    {
        Assert.Equal(RateLimitScope.DailyQuota, RateLimitClassifier.Classify(message));
    }

    [Theory]
    [InlineData("rate limit exceeded")]
    [InlineData("Error 429: too many requests")]
    [InlineData("""{"type":"error","error":{"type":"overloaded_error"}}""")]
    [InlineData("You have hit your session limit, resets 4pm (Europe/Stockholm)")]
    [InlineData("Claude usage limit reached")]
    public void Classify_ShortTermWording_ReturnsShortTerm(string message)
    {
        Assert.Equal(RateLimitScope.ShortTerm, RateLimitClassifier.Classify(message));
    }

    [Theory]
    [InlineData("fatal: could not read from remote repository")]
    [InlineData("Build failed with 2 errors")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Classify_UnrelatedFailures_ReturnsNone(string? message)
    {
        Assert.Equal(RateLimitScope.None, RateLimitClassifier.Classify(message));
    }

    [Fact]
    public void Classify_Lines_ReturnsStrongestScope()
    {
        string[] lines =
        [
            "Starting run",
            "rate limit exceeded, retrying",
            "API Error: Request rejected (429) - Too many tokens per day, please wait before trying again."
        ];

        Assert.Equal(RateLimitScope.DailyQuota, RateLimitClassifier.Classify(lines));
    }

    [Fact]
    public void Classify_Lines_WhenOnlyShortTermMatches_ReturnsShortTerm()
    {
        string[] lines = ["Starting run", "Error 429: too many requests", "Build failed with 2 errors"];

        Assert.Equal(RateLimitScope.ShortTerm, RateLimitClassifier.Classify(lines));
    }

    [Fact]
    public void Classify_Lines_WhenNothingMatches_ReturnsNone()
    {
        string[] lines = ["Starting run", "fatal: could not read from remote repository"];

        Assert.Equal(RateLimitScope.None, RateLimitClassifier.Classify(lines));
        Assert.Equal(RateLimitScope.None, RateLimitClassifier.Classify([]));
    }
}
