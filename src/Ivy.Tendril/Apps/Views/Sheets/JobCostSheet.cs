using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Jobs;

namespace Ivy.Tendril.Apps.Views.Sheets;

/// <summary>
/// Breaks one job's Cost and Tokens cells down into the buckets behind them: how many tokens of
/// each kind, what per-million rate applies, what that arithmetic comes to — and, crucially,
/// whether the charge actually came from that arithmetic or was reported by the agent CLI.
/// Shared by the Jobs app and the plan Details tab.
/// </summary>
public class JobCostSheet(string jobId, IJobService jobService) : ViewBase
{
    private const string NoValue = "—";

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

    public override object Build()
    {
        var pricingProvider = UseService<IModelPricingProvider>();

        var job = jobService.GetJob(jobId);
        if (job is null)
            return Text.P("Job not found.");

        var pricing = string.IsNullOrWhiteSpace(job.Model) ? null : pricingProvider.GetPricing(job.Model);

        var details = new
        {
            Model = job.Model ?? "",
            Provider = job.Provider,
            Type = job.Type,
            Status = job.Status.ToString(),
            Duration = job.DurationSeconds.HasValue ? $"{job.DurationSeconds}s" : "",
        }.ToDetails().RemoveEmpty();

        var buckets = BuildBuckets(job, pricing);

        if (buckets.Count == 0)
            return Layout.Vertical().Gap(4)
                   | details
                   | BuildNoBreakdownView(job);

        // Reasoning tokens are reported alongside (and by some providers within) the output bucket,
        // so they are shown for information but left out of the totals rather than double-counted.
        var counted = buckets.Where(b => b.CountsTowardTotal).ToList();
        var totalTokens = counted.Sum(b => b.Tokens);
        var computedCost = counted.Any(b => b.Rate.HasValue)
            ? counted.Sum(b => b.Tokens * (b.Rate ?? 0m) / 1_000_000m)
            : (decimal?)null;

        var table = buckets
            .Select(b => new UsageRow
            {
                Kind = b.Kind,
                Tokens = b.Tokens.ToString("N0"),
                RatePerMillion = b.Rate.HasValue ? $"${b.Rate.Value:N2}" : NoValue,
                Cost = b.Rate.HasValue ? $"${b.Tokens * b.Rate.Value / 1_000_000m:F4}" : NoValue,
            })
            .ToTable()
            .Header(x => x.Kind, "Token type")
            .Header(x => x.Tokens, "Tokens")
            .Header(x => x.RatePerMillion, "Rate / 1M")
            .Header(x => x.Cost, "Cost")
            .AlignContent(x => x.Tokens, Align.Right)
            .AlignContent(x => x.RatePerMillion, Align.Right)
            .AlignContent(x => x.Cost, Align.Right)
            .Totals(x => x.Kind, _ => "Total")
            .Totals(x => x.Tokens, _ => totalTokens.ToString("N0"))
            .Totals(x => x.Cost, _ => computedCost.HasValue ? $"${computedCost.Value:F4}" : NoValue)
            .Width(Size.Full());

        return Layout.Vertical().Gap(4)
               | details
               | (Layout.Vertical().Gap(2)
                  | Text.H4("Breakdown")
                  | table
                  | Text.Muted(BuildReconciliation(job, computedCost)))
               | (Layout.Vertical().Gap(2)
                  | Text.H4("Price list")
                  | Text.Muted(BuildPriceListSource(job, pricing))
                  | Text.Muted(
                      "The Tokens column in the Jobs table counts input + output only; cache tokens "
                      + "are listed here but excluded from it. Reasoning tokens are shown for "
                      + "information and excluded from the totals above to avoid double-counting "
                      + "output."));
    }

    /// <summary>
    /// One row of the breakdown, in raw (unformatted) form so the footer totals can be summed
    /// before anything is stringified.
    /// </summary>
    private readonly record struct Bucket(string Kind, int Tokens, decimal? Rate, bool CountsTowardTotal);

    private static List<Bucket> BuildBuckets(JobItem job, ModelPricing? pricing)
    {
        var buckets = new List<Bucket>();

        void Add(string kind, int? tokens, decimal? rate, bool countsTowardTotal = true)
        {
            if (tokens is > 0)
                buckets.Add(new Bucket(kind, tokens.Value, rate, countsTowardTotal));
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
    /// Shown when no per-bucket data was persisted: either the job has not been costed yet, or it
    /// predates the breakdown columns — in which case the totals it does have are still worth showing.
    /// </summary>
    private static object BuildNoBreakdownView(JobItem job)
    {
        if (job.Tokens is not > 0 && job.Cost is null)
        {
            var reason = job.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Timeout or JobStatus.Stopped
                ? "No usage data recorded for this job. Cost is calculated about 30 seconds after a job finishes, so a just-completed job may still be pending."
                : "No usage data recorded for this job yet. Cost is calculated about 30 seconds after the job finishes.";
            return Text.Muted(reason);
        }

        var totals = new List<string>();
        if (job.Tokens is > 0) totals.Add($"{job.Tokens.Value:N0} tokens (input + output)");
        if (job.Cost is not null) totals.Add($"${job.Cost.Value:F4}");

        return Layout.Vertical().Gap(2)
               | Text.Block(string.Join(" · ", totals))
               | Text.Muted(
                   "No per-token breakdown was recorded for this job — only the totals above. Jobs "
                   + "that completed before the breakdown was persisted have no bucket detail.");
    }

    /// <summary>
    /// States plainly whether the displayed cost came from the agent's own report or from the rates
    /// in the table, and surfaces the gap when the two disagree.
    /// </summary>
    private static string BuildReconciliation(JobItem job, decimal? computedCost)
    {
        if (job.Cost is null)
            return "No cost recorded for this job; the figures above are token counts only.";

        var charged = $"${job.Cost.Value:F4}";

        if (job.CostSource == JobCostSources.Computed)
            return $"Charged {charged}, computed from the rates above (the agent did not report a cost).";

        if (job.CostSource == JobCostSources.Agent)
        {
            var text = $"Charged {charged} as reported by the {job.Provider} CLI. The per-token rates "
                       + "above are Tendril's reference prices and were not used for this figure.";

            if (computedCost.HasValue && Math.Abs(computedCost.Value - job.Cost.Value) > 0.01m)
                text += $" Those rates would give ${computedCost.Value:F4}.";

            return text;
        }

        return $"Charged {charged}. The source of this figure was not recorded (it predates cost-source tracking).";
    }

    private static string BuildPriceListSource(JobItem job, ModelPricing? pricing)
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
    /// Maps a "Static catalog (claude)" source label back to the file the rates are written in.
    /// Returns null for any other source (e.g. a models.dev URL), which is already self-describing.
    /// </summary>
    private static string? ResolveCatalogFile(string? source)
    {
        const string prefix = "Static catalog (";
        if (source is null || !source.StartsWith(prefix, StringComparison.Ordinal) || !source.EndsWith(')'))
            return null;

        var agentId = source[prefix.Length..^1];
        return CatalogFolders.TryGetValue(agentId, out var folder)
            ? $"src/Ivy.Tendril.Agents/Providers/{folder}/{folder}ModelCatalog.cs"
            : null;
    }

    private sealed record UsageRow
    {
        public required string Kind { get; init; }
        public required string Tokens { get; init; }
        public required string RatePerMillion { get; init; }
        public required string Cost { get; init; }
    }
}
