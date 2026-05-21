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
}
