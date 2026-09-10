using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Test.Abstractions;

public class AgentUsageTests
{
    [Fact]
    public void AgentUsage_DefaultValues_AreZero()
    {
        var usage = new AgentUsage();

        Assert.Equal(0, usage.InputTokens);
        Assert.Equal(0, usage.OutputTokens);
        Assert.Equal(0, usage.CacheReadTokens);
        Assert.Equal(0, usage.CacheWriteTokens);
        Assert.Equal(0, usage.ReasoningTokens);
        Assert.Null(usage.CostUsd);
        Assert.Null(usage.PremiumRequests);
        Assert.Null(usage.Model);
        Assert.Null(usage.ModelBreakdown);
    }

    [Fact]
    public void AgentUsage_WithAllFields_RoundTrips()
    {
        var usage = new AgentUsage
        {
            InputTokens = 1000,
            OutputTokens = 500,
            CacheReadTokens = 200,
            CacheWriteTokens = 100,
            ReasoningTokens = 50,
            CostUsd = 0.05m,
            PremiumRequests = 1,
            Model = "claude-opus-4-6",
            ModelBreakdown =
            [
                new ModelUsageEntry
                {
                    Model = "claude-opus-4-6",
                    InputTokens = 1000,
                    OutputTokens = 500,
                    CostUsd = 0.05m,
                }
            ],
        };

        Assert.Equal(1000, usage.InputTokens);
        Assert.Equal(0.05m, usage.CostUsd);
        Assert.Single(usage.ModelBreakdown!);
        Assert.Equal("claude-opus-4-6", usage.ModelBreakdown[0].Model);
    }

    [Fact]
    public void ModelPricing_CreatesCorrectly()
    {
        var pricing = new ModelPricing
        {
            Model = "claude-sonnet-4-6",
            InputPerMillion = 3.0m,
            OutputPerMillion = 15.0m,
            CacheWritePerMillion = 3.75m,
            CacheReadPerMillion = 0.30m,
        };

        Assert.Equal("claude-sonnet-4-6", pricing.Model);
        Assert.Equal(3.0m, pricing.InputPerMillion);
        Assert.Equal(15.0m, pricing.OutputPerMillion);
    }

    [Fact]
    public void SessionCostResult_CreatesCorrectly()
    {
        var result = new SessionCostResult
        {
            SessionId = "s-123",
            AgentId = AgentId.Claude,
            Model = "claude-opus-4-6",
            InputTokens = 5000,
            OutputTokens = 2000,
            TotalCostUsd = 0.25m,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAt = DateTimeOffset.UtcNow,
        };

        Assert.Equal("s-123", result.SessionId);
        Assert.Equal(0.25m, result.TotalCostUsd);
        Assert.NotNull(result.CompletedAt);
    }

    [Fact]
    public void ModelUsageEntry_DefaultValues()
    {
        var entry = new ModelUsageEntry { Model = "test" };

        Assert.Equal(0, entry.InputTokens);
        Assert.Equal(0, entry.OutputTokens);
        Assert.Null(entry.CostUsd);
    }

    [Fact]
    public void AgentUsageWindow_ComputesRemainingPercent_FromUsedPercent()
    {
        var window = new AgentUsageWindow { WindowMinutes = 300, UsedPercent = 25.5 };

        Assert.Equal(25.5, window.UsedPercent);
        Assert.NotNull(window.RemainingPercent);
        Assert.Equal(74.5, window.RemainingPercent.Value, precision: 4);
    }

    [Fact]
    public void AgentUsageWindow_ComputesUsedPercent_FromRemainingPercent()
    {
        var window = new AgentUsageWindow { WindowMinutes = 300, RemainingPercent = 69.3 };

        Assert.Equal(69.3, window.RemainingPercent);
        Assert.NotNull(window.UsedPercent);
        Assert.Equal(30.7, window.UsedPercent.Value, precision: 4);
    }

    [Fact]
    public void AgentUsageWindow_WhenBothSet_PreservesBoth()
    {
        var window = new AgentUsageWindow
        {
            WindowMinutes = 300,
            UsedPercent = 30.0,
            RemainingPercent = 70.0,
        };

        Assert.Equal(30.0, window.UsedPercent);
        Assert.Equal(70.0, window.RemainingPercent);
    }

    [Fact]
    public void AgentUsageWindow_WhenNeitherSet_BothNull()
    {
        var window = new AgentUsageWindow { WindowMinutes = 300 };

        Assert.Null(window.UsedPercent);
        Assert.Null(window.RemainingPercent);
    }

    [Fact]
    public void AgentUsageWindow_ClampsValues_BetweenZeroAndOneHundred()
    {
        var high = new AgentUsageWindow { WindowMinutes = 300, UsedPercent = 120.0 };
        Assert.Equal(0.0, high.RemainingPercent);

        var low = new AgentUsageWindow { WindowMinutes = 300, UsedPercent = -10.0 };
        Assert.Equal(100.0, low.RemainingPercent);

        var highRem = new AgentUsageWindow { WindowMinutes = 300, RemainingPercent = 150.0 };
        Assert.Equal(0.0, highRem.UsedPercent);

        var lowRem = new AgentUsageWindow { WindowMinutes = 300, RemainingPercent = -20.0 };
        Assert.Equal(100.0, lowRem.UsedPercent);
    }
}
