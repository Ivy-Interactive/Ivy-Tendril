using Ivy.Tendril.Services.Jobs;
using Ivy.Tendril.Services.Telemetry;
using Xunit;

namespace Ivy.Tendril.Test.Services;

public class JobCostResolutionTests
{
    // #1426: a timed-out run reports token usage but no cost inline; the pricing fallback derives
    // the real charge from tokens × model price. The derived cost must win over the inline $0.
    [Fact]
    public void ResolveJobCost_InlineTokensButZeroCost_UsesPricedCost()
    {
        var priced = new CostCalculation { TotalTokens = 1549, TotalCost = 0.0058 };

        var (tokens, cost) = JobCompletionHandler.ResolveJobCost((1549, 0m), priced);

        Assert.Equal(1549, tokens);
        Assert.Equal(0.0058m, cost);
    }

    [Fact]
    public void ResolveJobCost_PositiveInlineCost_KeepsInlineCost()
    {
        var (tokens, cost) = JobCompletionHandler.ResolveJobCost((1000, 0.02m), priced: null);

        Assert.Equal(1000, tokens);
        Assert.Equal(0.02m, cost);
    }

    [Fact]
    public void ResolveJobCost_NoPriceableCost_KeepsTokensAndLeavesCostNull()
    {
        // No session file / un-priceable model: priced is empty. Never surface a misleading $0.0000.
        var priced = new CostCalculation { TotalTokens = 0, TotalCost = 0.0 };

        var (tokens, cost) = JobCompletionHandler.ResolveJobCost((1549, 0m), priced);

        Assert.Equal(1549, tokens);
        Assert.Null(cost);
    }

    [Fact]
    public void ResolveJobCost_NoInline_UsesPricedTokensAndCost()
    {
        var priced = new CostCalculation { TotalTokens = 1500, TotalCost = 0.025 };

        var (tokens, cost) = JobCompletionHandler.ResolveJobCost(inline: null, priced);

        Assert.Equal(1500, tokens);
        Assert.Equal(0.025m, cost);
    }

    [Fact]
    public void ResolveJobCost_PrefersPricedTokenCount_WhenPresent()
    {
        // Pricing re-parses the full session (incl. subagents), so its token count wins.
        var priced = new CostCalculation { TotalTokens = 4200, TotalCost = 0.031 };

        var (tokens, cost) = JobCompletionHandler.ResolveJobCost((1549, 0m), priced);

        Assert.Equal(4200, tokens);
        Assert.Equal(0.031m, cost);
    }
}
