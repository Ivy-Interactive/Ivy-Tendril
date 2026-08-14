using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Apps.Views.Sheets;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Jobs;
using Ivy.Tendril.Services.Telemetry;
using Xunit;

namespace Ivy.Tendril.Test.Services;

public class JobCostResolutionTests
{
    private static AgentUsage Inline(int input, int output, decimal cost, string? model = null) => new()
    {
        InputTokens = input,
        OutputTokens = output,
        CostUsd = cost,
        Model = model,
    };

    // #1426: a timed-out run reports token usage but no cost inline; the pricing fallback derives
    // the real charge from tokens × model price. The derived cost must win over the inline $0.
    [Fact]
    public void ResolveJobCost_InlineTokensButZeroCost_UsesPricedCost()
    {
        var priced = new CostCalculation { TotalTokens = 1549, TotalCost = 0.0058 };

        var usage = JobCompletionHandler.ResolveJobCost(Inline(1000, 549, 0m), priced);

        Assert.Equal(1549, usage.Tokens);
        Assert.Equal(0.0058m, usage.Cost);
        Assert.Equal(JobCostSources.Computed, usage.CostSource);
    }

    [Fact]
    public void ResolveJobCost_PositiveInlineCost_KeepsInlineCost()
    {
        var usage = JobCompletionHandler.ResolveJobCost(Inline(700, 300, 0.02m), priced: null);

        Assert.Equal(1000, usage.Tokens);
        Assert.Equal(0.02m, usage.Cost);
        Assert.Equal(JobCostSources.Agent, usage.CostSource);
    }

    [Fact]
    public void ResolveJobCost_NoPriceableCost_KeepsTokensAndLeavesCostNull()
    {
        // No session file / un-priceable model: priced is empty. Never surface a misleading $0.0000.
        var priced = new CostCalculation { TotalTokens = 0, TotalCost = 0.0 };

        var usage = JobCompletionHandler.ResolveJobCost(Inline(1000, 549, 0m), priced);

        Assert.Equal(1549, usage.Tokens);
        Assert.Null(usage.Cost);
        Assert.Null(usage.CostSource);
    }

    [Fact]
    public void ResolveJobCost_NoInline_UsesPricedTokensAndCost()
    {
        var priced = new CostCalculation { TotalTokens = 1500, TotalCost = 0.025 };

        var usage = JobCompletionHandler.ResolveJobCost(inline: null, priced);

        Assert.Equal(1500, usage.Tokens);
        Assert.Equal(0.025m, usage.Cost);
        Assert.Equal(JobCostSources.Computed, usage.CostSource);
    }

    [Fact]
    public void ResolveJobCost_PrefersPricedTokenCount_WhenPresent()
    {
        // Pricing re-parses the full session (incl. subagents), so its token count wins.
        var priced = new CostCalculation { TotalTokens = 4200, TotalCost = 0.031 };

        var usage = JobCompletionHandler.ResolveJobCost(Inline(1000, 549, 0m), priced);

        Assert.Equal(4200, usage.Tokens);
        Assert.Equal(0.031m, usage.Cost);
    }

    [Fact]
    public void ResolveJobCost_PrefersPricedBuckets_OverInlineBuckets()
    {
        // Same reason as the token count: the session parse sees subagent traffic the inline
        // ResultEvent never reports.
        var inline = new AgentUsage
        {
            InputTokens = 1000,
            OutputTokens = 549,
            CacheReadTokens = 10,
            CacheWriteTokens = 5,
        };
        var priced = new CostCalculation
        {
            TotalTokens = 4200,
            TotalCost = 0.031,
            InputTokens = 3000,
            OutputTokens = 1200,
            CacheReadTokens = 90_000,
            CacheWriteTokens = 400,
        };

        var usage = JobCompletionHandler.ResolveJobCost(inline, priced);

        Assert.Equal(3000, usage.InputTokens);
        Assert.Equal(1200, usage.OutputTokens);
        Assert.Equal(90_000, usage.CacheReadTokens);
        Assert.Equal(400, usage.CacheWriteTokens);
    }

    [Fact]
    public void ResolveJobCost_PricedHasNoBuckets_FallsBackToInlineBuckets()
    {
        var inline = new AgentUsage
        {
            InputTokens = 1000,
            OutputTokens = 549,
            CacheReadTokens = 88_000,
            CacheWriteTokens = 120,
            ReasoningTokens = 64,
            CostUsd = 0.02m,
        };

        var usage = JobCompletionHandler.ResolveJobCost(inline, new CostCalculation());

        Assert.Equal(1000, usage.InputTokens);
        Assert.Equal(549, usage.OutputTokens);
        Assert.Equal(88_000, usage.CacheReadTokens);
        Assert.Equal(120, usage.CacheWriteTokens);
        Assert.Equal(64, usage.ReasoningTokens);
    }

    [Fact]
    public void ResolveJobCost_CacheOnlyPricedSession_StillUsesPricedBuckets()
    {
        // TotalTokens is input+output, so a cache-only session has TotalTokens == 0 while still
        // holding real bucket data — the bucket precedence must not key off TotalTokens.
        var priced = new CostCalculation { CacheReadTokens = 50_000, TotalCost = 0.005 };

        var usage = JobCompletionHandler.ResolveJobCost(Inline(10, 5, 0m), priced);

        Assert.Equal(50_000, usage.CacheReadTokens);
        Assert.Equal(0, usage.InputTokens);
    }

    [Fact]
    public void ResolveJobCost_CarriesModel_PreferringTheSessionParse()
    {
        var priced = new CostCalculation { TotalTokens = 1500, TotalCost = 0.02, Model = "claude-opus-5" };

        var usage = JobCompletionHandler.ResolveJobCost(Inline(10, 5, 0m, model: "inline-model"), priced);

        Assert.Equal("claude-opus-5", usage.Model);
    }

    [Fact]
    public void ResolveJobCost_NoSessionModel_FallsBackToInlineModel()
    {
        var usage = JobCompletionHandler.ResolveJobCost(
            Inline(10, 5, 0.01m, model: "gpt-5-codex"), new CostCalculation());

        Assert.Equal("gpt-5-codex", usage.Model);
    }

    [Fact]
    public void ResolveJobCost_NoUsageAtAll_LeavesBucketsAndCostNull()
    {
        var usage = JobCompletionHandler.ResolveJobCost(inline: null, priced: null);

        Assert.Equal(0, usage.Tokens);
        Assert.Null(usage.Cost);
        Assert.Null(usage.CostSource);
        Assert.Null(usage.Model);
        Assert.Null(usage.InputTokens);
        Assert.Null(usage.OutputTokens);
        Assert.Null(usage.CacheReadTokens);
        Assert.Null(usage.CacheWriteTokens);
        Assert.Null(usage.ReasoningTokens);
    }

    [Fact]
    public void ApplyTo_WritesBreakdownOntoJob()
    {
        var job = new JobItem { Id = "j1", Type = "ExecutePlan", PlanFile = "p", Project = "Tendril" };

        JobCompletionHandler.ResolveJobCost(
                new AgentUsage
                {
                    InputTokens = 1000,
                    OutputTokens = 500,
                    CacheReadTokens = 90_000,
                    CacheWriteTokens = 300,
                    ReasoningTokens = 42,
                    CostUsd = 1.25m,
                    Model = "claude-opus-5",
                },
                priced: null)
            .ApplyTo(job);

        Assert.Equal(1500, job.Tokens);
        Assert.Equal(1.25m, job.Cost);
        Assert.Equal(1000, job.InputTokens);
        Assert.Equal(500, job.OutputTokens);
        Assert.Equal(90_000, job.CacheReadTokens);
        Assert.Equal(300, job.CacheWriteTokens);
        Assert.Equal(42, job.ReasoningTokens);
        Assert.Equal("claude-opus-5", job.Model);
        Assert.Equal(JobCostSources.Agent, job.CostSource);
    }

    [Fact]
    public void ApplyTo_UnknownModel_KeepsTheModelRecordedAtLaunch()
    {
        var job = new JobItem
        {
            Id = "j1",
            Type = "ExecutePlan",
            PlanFile = "p",
            Project = "Tendril",
            Model = "claude-opus-5",
        };

        JobCompletionHandler.ResolveJobCost(Inline(1000, 500, 1.25m), priced: null).ApplyTo(job);

        Assert.Equal("claude-opus-5", job.Model);
    }

    // The breakdown sheet's whole point is that it does not overstate what Tendril knows, so the
    // statements it makes about provenance are asserted here rather than left to inspection.

    private static readonly ModelPricing StaticClaudePricing = new()
    {
        Model = "claude-opus-5",
        InputPerMillion = 3m,
        OutputPerMillion = 15m,
        CacheReadPerMillion = 0.3m,
        CacheWritePerMillion = 3.75m,
        Source = "Static catalog (claude)",
    };

    private static JobItem CostedJob(string? costSource, decimal? cost = 1.25m) => new()
    {
        Id = "j1",
        Type = "ExecutePlan",
        PlanFile = "p",
        Project = "Tendril",
        Provider = "claude",
        Model = "claude-opus-5",
        Cost = cost,
        Tokens = 1500,
        InputTokens = 1000,
        OutputTokens = 500,
        CacheReadTokens = 90_000,
        CacheWriteTokens = 300,
        ReasoningTokens = 42,
        CostSource = costSource,
    };

    [Fact]
    public void Sheet_BuildBuckets_ExcludesReasoningFromTheTotals()
    {
        var buckets = JobCostSheet.BuildBuckets(CostedJob(JobCostSources.Agent), StaticClaudePricing);

        Assert.Equal(
            new[] { "Input", "Output", "Cache read", "Cache write", "Reasoning" },
            buckets.Select(b => b.Kind));

        // Reasoning is reported alongside (and by some providers inside) output, so counting it
        // would inflate the total.
        var reasoning = buckets.Single(b => b.Kind == "Reasoning");
        Assert.False(reasoning.CountsTowardTotal);
        Assert.Null(reasoning.Rate);
        Assert.All(buckets.Where(b => b.Kind != "Reasoning"), b => Assert.True(b.CountsTowardTotal));
    }

    [Fact]
    public void Sheet_BuildBuckets_OmitsEmptyBucketsAndRatesWhenUnpriced()
    {
        var job = CostedJob(JobCostSources.Agent);
        job.CacheWriteTokens = 0;
        job.ReasoningTokens = null;

        var buckets = JobCostSheet.BuildBuckets(job, pricing: null);

        Assert.Equal(new[] { "Input", "Output", "Cache read" }, buckets.Select(b => b.Kind));
        Assert.All(buckets, b => Assert.Null(b.Rate));
    }

    [Fact]
    public void Sheet_Reconciliation_AgentReportedCost_DisclaimsTheLocalRates()
    {
        var text = JobCostSheet.BuildReconciliation(CostedJob(JobCostSources.Agent), computedCost: 1.2505m);

        Assert.Contains("$1.2500", text);
        Assert.Contains("as reported by the claude CLI", text);
        Assert.Contains("were not used for this figure", text);
        // Within a cent of the charge, so the computed figure is not worth surfacing.
        Assert.DoesNotContain("Those rates would give", text);
    }

    [Fact]
    public void Sheet_Reconciliation_AgentCostFarFromComputed_ShowsTheGap()
    {
        var text = JobCostSheet.BuildReconciliation(CostedJob(JobCostSources.Agent), computedCost: 0.4m);

        Assert.Contains("Those rates would give $0.4000", text);
    }

    [Fact]
    public void Sheet_Reconciliation_ComputedCost_SaysItCameFromTheRates()
    {
        var text = JobCostSheet.BuildReconciliation(CostedJob(JobCostSources.Computed), computedCost: 1.25m);

        Assert.Contains("computed from the rates above", text);
        Assert.DoesNotContain("were not used", text);
    }

    [Fact]
    public void Sheet_Reconciliation_UnknownSource_DoesNotClaimEitherOrigin()
    {
        // Jobs costed before CostSource existed must not be described as agent-reported.
        var text = JobCostSheet.BuildReconciliation(CostedJob(costSource: null), computedCost: 1.25m);

        Assert.Contains("was not recorded", text);
        Assert.DoesNotContain("as reported by", text);
        Assert.DoesNotContain("computed from the rates", text);
    }

    [Fact]
    public void Sheet_Reconciliation_NoCost_SaysSo()
    {
        var text = JobCostSheet.BuildReconciliation(CostedJob(costSource: null, cost: null), computedCost: null);

        Assert.Contains("No cost recorded", text);
    }

    [Fact]
    public void Sheet_PriceListSource_NamesTheStaticCatalogFile()
    {
        var text = JobCostSheet.BuildPriceListSource(CostedJob(JobCostSources.Agent), StaticClaudePricing);

        Assert.Contains("Static catalog (claude)", text);
        Assert.Contains("src/Ivy.Tendril.Agents/Providers/Claude/ClaudeModelCatalog.cs", text);
    }

    [Fact]
    public void Sheet_PriceListSource_NoMatchingEntry_SaysNoRatesApplied()
    {
        var text = JobCostSheet.BuildPriceListSource(CostedJob(JobCostSources.Agent), pricing: null);

        Assert.Contains("No price list entry matches 'claude-opus-5'", text);
    }

    [Theory]
    [InlineData("Static catalog (claude)", "src/Ivy.Tendril.Agents/Providers/Claude/ClaudeModelCatalog.cs")]
    [InlineData("Static catalog (opencode)", "src/Ivy.Tendril.Agents/Providers/OpenCode/OpenCodeModelCatalog.cs")]
    [InlineData("Static catalog (nonesuch)", null)]
    [InlineData("https://models.dev", null)]
    [InlineData(null, null)]
    public void Sheet_ResolveCatalogFile(string? source, string? expected)
    {
        Assert.Equal(expected, JobCostSheet.ResolveCatalogFile(source));
    }
}
