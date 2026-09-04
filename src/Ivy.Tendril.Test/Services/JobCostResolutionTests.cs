using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Runtime;
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

    [Fact]
    public void Sheet_Profile_ComesFromTheJobAndIsCapitalised()
    {
        var job = CostedJob(JobCostSources.Agent);
        job.ExecutionProfile = "deep";

        Assert.Equal("Deep", JobCostModelBuilder.FormatProfile(job));
    }

    [Fact]
    public void Sheet_Profile_IsAbsentWhenTheJobRecordedNone()
    {
        // Jobs launched before migration 020 carry no profile, and are not backfilled from the
        // plan: the plan's profile can have been edited since, so it would misreport the run.
        var job = CostedJob(JobCostSources.Agent);

        Assert.Null(JobCostModelBuilder.FormatProfile(job));
    }

    [Fact]
    public void Sheet_Effort_ComesFromTheJobAndIsCapitalised()
    {
        var job = CostedJob(JobCostSources.Agent);
        job.Effort = "high";

        Assert.Equal("High", Build(job).Effort);
    }

    [Fact]
    public void Sheet_Effort_IsAbsentForAnAgentWithNoEffortControl()
    {
        Assert.Null(Build(CostedJob(JobCostSources.Agent)).Effort);
    }

    [Fact]
    public void Sheet_Signature_ChangesWhenTheEffortIsRecordedAtLaunch()
    {
        var job = UncostedJob();
        var before = JobCostModelBuilder.BuildSignature(job);

        job.Effort = "high";

        Assert.NotEqual(before, JobCostModelBuilder.BuildSignature(job));
    }

    [Fact]
    public void Sheet_Signature_ChangesWhenTheProfileIsRecordedAtLaunch()
    {
        var job = UncostedJob();
        var before = JobCostModelBuilder.BuildSignature(job);

        job.ExecutionProfile = "deep";

        Assert.NotEqual(before, JobCostModelBuilder.BuildSignature(job));
    }

    private static readonly ModelPricing StaticClaudePricing = new()
    {
        Model = "claude-opus-5",
        InputPerMillion = 3m,
        OutputPerMillion = 15m,
        CacheReadPerMillion = 0.3m,
        CacheWritePerMillion = 3.75m,
        Source = "Static catalog (claude)",
    };

    /// <summary>Builds the model against a price list holding only <see cref="StaticClaudePricing" />.</summary>
    private static JobCostModel Build(JobItem job) =>
        JobCostModelBuilder.Build(job, new ModelPricingProvider([StaticClaudePricing]));

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
        var buckets = JobCostModelBuilder.BuildBuckets(CostedJob(JobCostSources.Agent), StaticClaudePricing);

        Assert.Equal(
            new[] { "Input", "Output", "Cache Read", "Cache Write", "Reasoning" },
            buckets.Select(b => b.Kind));

        // Reasoning is reported alongside (and by some providers inside) output, so counting it
        // would inflate the total.
        var reasoning = buckets.Single(b => b.Kind == "Reasoning");
        Assert.False(reasoning.CountsTowardTotal);
        Assert.Null(reasoning.RatePerMillion);
        Assert.All(buckets.Where(b => b.Kind != "Reasoning"), b => Assert.True(b.CountsTowardTotal));
    }

    [Fact]
    public void Sheet_BuildBuckets_OmitsEmptyBucketsAndRatesWhenUnpriced()
    {
        var job = CostedJob(JobCostSources.Agent);
        job.CacheWriteTokens = 0;
        job.ReasoningTokens = null;

        var buckets = JobCostModelBuilder.BuildBuckets(job, pricing: null);

        Assert.Equal(new[] { "Input", "Output", "Cache Read" }, buckets.Select(b => b.Kind));
        Assert.All(buckets, b => Assert.Null(b.RatePerMillion));
    }

    [Theory]
    [InlineData(JobCostSources.Agent, 1.25, 1.25)]
    [InlineData(JobCostSources.Computed, 1.25, null)]
    [InlineData(null, 1.25, null)]
    public void Sheet_AgentReportedCost_OnlySetWhenTheAgentActuallyReportedOne(
        string? costSource, double charged, double? expected)
    {
        // A charge whose source was never recorded must not be presented as the agent's figure.
        var model = Build(CostedJob(costSource, (decimal)charged));

        Assert.Equal((decimal?)expected, model.AgentReportedCost);
        Assert.Equal((decimal)charged, model.ChargedCost);
        Assert.Equal(costSource, model.CostSource);
    }

    [Fact]
    public void Sheet_PriceList_StaticCatalog_IsAttributedToTendrilWithNothingToLinkTo()
    {
        var (priceList, url) = JobCostModelBuilder.ResolvePriceList(StaticClaudePricing);

        Assert.Equal("Tendril", priceList);
        Assert.Null(url);
    }

    [Fact]
    public void Sheet_PriceList_RefreshedEntry_LinksToModelsDev()
    {
        var refreshed = StaticClaudePricing with { Source = ModelsDevPricingSource.SourceUrl };

        var (priceList, url) = JobCostModelBuilder.ResolvePriceList(refreshed);

        // The link goes to the site a reader can actually read, not the JSON the rates came from.
        Assert.Equal("models.dev", priceList);
        Assert.Equal("https://models.dev/", url);
        Assert.NotEqual(ModelsDevPricingSource.SourceUrl, url);
    }

    [Fact]
    public void Sheet_PriceList_NoMatchingEntry_IsEmptySoTheRowIsDropped()
    {
        var (priceList, url) = JobCostModelBuilder.ResolvePriceList(pricing: null);

        Assert.Equal("", priceList);
        Assert.Null(url);
    }

    // A sheet opened on a just-completed job renders the empty state; the cost lands ~30s later on a
    // background task. UseJobUpdates re-renders on that, but only when the signature changes.
    [Fact]
    public void Sheet_Signature_ChangesWhenTheDelayedCostLands()
    {
        var job = UncostedJob();
        var before = JobCostModelBuilder.BuildSignature(job);

        job.Cost = 1.25m;
        job.CostSource = JobCostSources.Agent;
        job.Tokens = 1500;
        job.InputTokens = 1000;
        job.OutputTokens = 500;

        Assert.NotEqual(before, JobCostModelBuilder.BuildSignature(job));
    }

    [Fact]
    public void Sheet_Signature_IgnoresFieldsItDoesNotRender()
    {
        var job = CostedJob(JobCostSources.Agent);
        var before = JobCostModelBuilder.BuildSignature(job);

        // JobPropertyChanged also fires on every status report from every running job; the cost
        // sheet shows none of this, so it must not rebuild for it.
        job.StatusMessage = "Verifying: DotnetBuild";
        job.LastOutputAt = DateTime.UtcNow;

        Assert.Equal(before, JobCostModelBuilder.BuildSignature(job));
    }

    [Fact]
    public void DebugSheet_Signature_ChangesWhenTheDelayedCostLands()
    {
        var job = UncostedJob();
        var before = JobDebugSheet.BuildSignature(job);

        job.Cost = 1.25m;
        job.Tokens = 1500;

        Assert.NotEqual(before, JobDebugSheet.BuildSignature(job));
    }

    [Fact]
    public void DebugSheet_Signature_TracksTheStatusMessageItRenders()
    {
        var job = CostedJob(JobCostSources.Agent);
        var before = JobDebugSheet.BuildSignature(job);

        job.StatusMessage = "Verifying: DotnetBuild";

        Assert.NotEqual(before, JobDebugSheet.BuildSignature(job));
    }

    private static JobItem UncostedJob() => new()
    {
        Id = "j1",
        Type = "ExecutePlan",
        PlanFile = "p",
        Project = "Tendril",
        Provider = "claude",
        Model = "claude-opus-5",
        Status = JobStatus.Completed,
    };
}
