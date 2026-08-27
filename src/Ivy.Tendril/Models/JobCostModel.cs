using System.Globalization;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services.Jobs;

namespace Ivy.Tendril.Models;

/// <summary>
///     One row of a job's token breakdown, in raw form so totals can be summed before anything is
///     stringified.
/// </summary>
/// <param name="Kind">Token bucket — Input, Output, Cache read, Cache write, Reasoning.</param>
/// <param name="Tokens">How many tokens of that kind the job used.</param>
/// <param name="RatePerMillion">The price list's rate, or null when none applies to this bucket.</param>
/// <param name="CountsTowardTotal">
///     False for reasoning tokens, which some providers report inside the output bucket — counting
///     them again would double the output.
/// </param>
public readonly record struct JobCostBucket(
    string Kind,
    int Tokens,
    decimal? RatePerMillion,
    bool CountsTowardTotal);

/// <summary>
///     Everything <c>JobCostSheet</c> renders about one job, resolved once by
///     <see cref="JobCostModelBuilder" /> so the sheet does no lookups of its own.
///     <para>
///         Numbers are raw — dollars and token counts are formatted at render time. The prose fields
///         are not formatting but findings: what the charge was reconciled against, and where the
///         rates came from. Both are conclusions drawn from job state, so they are settled here.
///     </para>
/// </summary>
public sealed record JobCostModel
{
    /// <summary>The model that ran, e.g. <c>claude-opus-4</c>. Empty when the job recorded none.</summary>
    public required string Model { get; init; }

    /// <summary>The agent CLI behind the job, e.g. <c>claude</c>.</summary>
    public required string Provider { get; init; }

    /// <summary>Promptware name, e.g. <c>ExecutePlan</c>.</summary>
    public required string Type { get; init; }

    /// <summary>
    ///     The execution profile the job ran under — Deep, Balanced — as recorded at launch. Null for
    ///     a job that predates <c>Migration_020_JobsExecutionProfile</c>, and for one no profile
    ///     applied to; the row is dropped rather than claiming a profile that never applied.
    /// </summary>
    public string? Profile { get; init; }

    /// <summary>Per-bucket usage, in document order. Empty when nothing was recorded per bucket.</summary>
    public IReadOnlyList<JobCostBucket> Buckets { get; init; } = [];

    /// <summary>Sum of the buckets that count, so reasoning tokens stay out of it.</summary>
    public int TotalTokens { get; init; }

    /// <summary>
    ///     What the rates in the price list come to for this job, or null when no rate matched. This
    ///     is not necessarily what was charged — see <see cref="Reconciliation" />.
    /// </summary>
    public decimal? ComputedCost { get; init; }

    /// <summary>Whether there is a per-bucket breakdown to show at all.</summary>
    public bool HasBreakdown => Buckets.Count > 0;

    /// <summary>Whether the job carries any usage figure, breakdown or not.</summary>
    public bool HasAnyUsage => HasBreakdown || TotalsOnlyTokens is > 0 || TotalsOnlyCost is not null;

    /// <summary>Token total for a job that predates the per-bucket columns.</summary>
    public int? TotalsOnlyTokens { get; init; }

    /// <summary>Charged cost for a job that predates the per-bucket columns.</summary>
    public decimal? TotalsOnlyCost { get; init; }

    /// <summary>Why there is nothing to show, when there is nothing to show.</summary>
    public string? NoUsageReason { get; init; }

    /// <summary>Whether the charge came from the agent's report or from the rates, and any gap.</summary>
    public required string Reconciliation { get; init; }

    /// <summary>Where the rates came from, and the file they are written in when that applies.</summary>
    public required string PriceListSource { get; init; }
}

/// <summary>
///     Resolves everything <c>JobCostSheet</c> needs into a <see cref="JobCostModel" />: the job, its
///     price list entry, the plan's execution profile, and the two findings the sheet states in prose.
///     <para>
///         Kept out of the sheet so the arithmetic and the wording can be tested without rendering
///         anything, and so the sheet does no lookups while building a view.
///     </para>
/// </summary>
public static class JobCostModelBuilder
{
    /// <summary>Folder + type-name prefix of each provider's hardcoded catalog, keyed by agent id.</summary>
    private static readonly Dictionary<string, string> CatalogFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["antigravity"] = "Antigravity",
        ["claude"] = "Claude",
        ["codex"] = "Codex",
        ["copilot"] = "Copilot",
        ["opencode"] = "OpenCode",
        ["ivy"] = "Ivy",
    };

    /// <summary>
    ///     Builds the model for <paramref name="jobId" />, or null when no such job exists — a job can
    ///     be cleared while its sheet is open.
    /// </summary>
    public static JobCostModel? Build(
        string jobId,
        IJobService jobService,
        IModelPricingProvider pricingProvider)
    {
        var job = jobService.GetJob(jobId);
        return job is null ? null : Build(job, pricingProvider);
    }

    /// <summary>As <see cref="Build(string,IJobService,IModelPricingProvider)" />, from a job already in hand.</summary>
    public static JobCostModel Build(JobItem job, IModelPricingProvider pricingProvider)
    {
        var pricing = string.IsNullOrWhiteSpace(job.Model) ? null : pricingProvider.GetPricing(job.Model);
        var buckets = BuildBuckets(job, pricing);

        // Reasoning tokens are reported alongside (and by some providers within) the output bucket,
        // so they are shown for information but left out of the totals rather than double-counted.
        var counted = buckets.Where(b => b.CountsTowardTotal).ToList();
        var computedCost = counted.Any(b => b.RatePerMillion.HasValue)
            ? counted.Sum(b => b.Tokens * (b.RatePerMillion ?? 0m) / 1_000_000m)
            : (decimal?)null;

        return new JobCostModel
        {
            Model = job.Model ?? "",
            Provider = job.Provider,
            Type = job.Type,
            Profile = FormatProfile(job),
            Buckets = buckets,
            TotalTokens = counted.Sum(b => b.Tokens),
            ComputedCost = computedCost,
            TotalsOnlyTokens = buckets.Count == 0 ? job.Tokens : null,
            TotalsOnlyCost = buckets.Count == 0 ? job.Cost : null,
            NoUsageReason = buckets.Count == 0 ? BuildNoUsageReason(job) : null,
            Reconciliation = BuildReconciliation(job, computedCost),
            PriceListSource = BuildPriceListSource(job, pricing),
        };
    }

    /// <summary>
    ///     The job fields the sheet renders, joined so <see cref="Hooks.UseJobUpdatesExtensions.UseJobUpdates" />
    ///     can re-render only when one of them actually changes.
    ///     <para>
    ///         Status is here even though it is not shown: it picks the wording of
    ///         <see cref="JobCostModel.NoUsageReason" />, so a job finishing changes what the sheet
    ///         says. Duration is not, because nothing renders it.
    ///     </para>
    /// </summary>
    public static string BuildSignature(JobItem job) => string.Create(CultureInfo.InvariantCulture,
        $"{job.Status};{job.Model};{job.Provider};{job.Type};{job.ExecutionProfile};{job.Cost};{job.CostSource};{job.Tokens};{job.InputTokens};{job.OutputTokens};{job.CacheReadTokens};{job.CacheWriteTokens};{job.ReasoningTokens}");

    /// <summary>
    ///     The profile the job recorded at launch, capitalised for display. Read from the job rather
    ///     than from the plan: the plan's profile can be edited after a run, and only ExecutePlan and
    ///     RetryPlan ever took it from there — so re-reading it would misattribute on both counts.
    /// </summary>
    internal static string? FormatProfile(JobItem job) =>
        FormatHelper.FormatExecutionProfile(job.ExecutionProfile);

    internal static List<JobCostBucket> BuildBuckets(JobItem job, ModelPricing? pricing)
    {
        var buckets = new List<JobCostBucket>();

        void Add(string kind, int? tokens, decimal? rate, bool countsTowardTotal = true)
        {
            if (tokens is > 0)
                buckets.Add(new JobCostBucket(kind, tokens.Value, rate, countsTowardTotal));
        }

        Add("Input", job.InputTokens, pricing?.InputPerMillion);
        Add("Output", job.OutputTokens, pricing?.OutputPerMillion);
        Add("Cache read", job.CacheReadTokens, pricing?.CacheReadPerMillion);
        Add("Cache write", job.CacheWriteTokens, pricing?.CacheWritePerMillion);
        // No reasoning rate exists in ModelPricing, so this row always renders "—" for rate/cost.
        Add("Reasoning", job.ReasoningTokens, rate: null, countsTowardTotal: false);

        return buckets;
    }

    /// <summary>
    ///     Why there is nothing to show — distinguishing a job that has not been costed yet from one
    ///     that finished and never was. Null when the job carries totals worth showing anyway.
    /// </summary>
    private static string? BuildNoUsageReason(JobItem job)
    {
        if (job.Tokens is > 0 || job.Cost is not null)
            return null;

        return job.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Timeout or JobStatus.Stopped
            ? "No usage data recorded for this job. Cost is calculated about 30 seconds after a job finishes, so a just-completed job may still be pending."
            : "No usage data recorded for this job yet. Cost is calculated about 30 seconds after the job finishes.";
    }

    /// <summary>
    ///     States plainly whether the displayed cost came from the agent's own report or from the rates
    ///     in the table, and surfaces the gap when the two disagree.
    /// </summary>
    internal static string BuildReconciliation(JobItem job, decimal? computedCost)
    {
        if (job.Cost is null)
            return "No cost recorded for this job; the figures above are token counts only.";

        var charged = FormatHelper.FormatCost(job.Cost.Value, decimals: 4);

        if (job.CostSource == JobCostSources.Computed)
            return $"Charged {charged}, computed from the rates above (the agent did not report a cost).";

        if (job.CostSource == JobCostSources.Agent)
        {
            var text = $"Charged {charged} as reported by the {job.Provider} CLI. The per-token rates "
                       + "above are Tendril's reference prices and were not used for this figure.";

            if (computedCost.HasValue && Math.Abs(computedCost.Value - job.Cost.Value) > 0.01m)
                text += $" Those rates would give {FormatHelper.FormatCost(computedCost.Value, decimals: 4)}.";

            return text;
        }

        return $"Charged {charged}. The source of this figure was not recorded (it predates cost-source tracking).";
    }

    internal static string BuildPriceListSource(JobItem job, ModelPricing? pricing)
    {
        if (pricing is null)
        {
            var model = string.IsNullOrWhiteSpace(job.Model) ? "this job's model" : $"'{job.Model}'";
            return $"No price list entry matches {model}, so no rates could be applied.";
        }

        var source = string.IsNullOrWhiteSpace(pricing.Source) ? "unknown" : pricing.Source;
        var text = $"Rates for '{pricing.Model}' come from: {source}.";

        var catalogFile = ResolveCatalogFile(pricing.Source);
        if (catalogFile is not null)
            text += $" They are hardcoded in {catalogFile}.";

        return text;
    }

    /// <summary>
    ///     Maps a "Static catalog (claude)" source label back to the file the rates are written in.
    ///     Returns null for any other source (e.g. a models.dev URL), which is already self-describing.
    /// </summary>
    internal static string? ResolveCatalogFile(string? source)
    {
        const string prefix = "Static catalog (";
        if (source is null || !source.StartsWith(prefix, StringComparison.Ordinal) || !source.EndsWith(')'))
            return null;

        var agentId = source[prefix.Length..^1];
        return CatalogFolders.TryGetValue(agentId, out var folder)
            ? $"src/Ivy.Tendril.Agents/Providers/{folder}/{folder}ModelCatalog.cs"
            : null;
    }
}
