using System.Globalization;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Hooks;
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

    /// <summary>
    /// Every number here is presented as US dollars or a raw token count, so it is formatted with
    /// the invariant culture — see <see cref="FormatHelper" />, which is where the Jobs UI's dollar
    /// formatting lives.
    /// </summary>
    private static string Usd(decimal value) => FormatHelper.FormatCost(value, decimals: 4);

    /// <summary>A per-million rate, grouped: "$1,234.56".</summary>
    private static string Rate(decimal value) => "$" + value.ToString("N2", CultureInfo.InvariantCulture);

    private static string Count(int value) => FormatHelper.FormatCount(value);

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

        // Cost lands about 30 seconds after the job finishes, so a sheet opened on a just-completed
        // job must pick it up rather than stay on the empty state until it is reopened.
        Context.UseJobUpdates(jobService, jobId, BuildSignature);

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
                Tokens = Count(b.Tokens),
                RatePerMillion = b.Rate.HasValue ? Rate(b.Rate.Value) : NoValue,
                Cost = b.Rate.HasValue ? Usd(b.Tokens * b.Rate.Value / 1_000_000m) : NoValue,
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
            .Totals(x => x.Tokens, _ => Count(totalTokens))
            .Totals(x => x.Cost, _ => computedCost.HasValue ? Usd(computedCost.Value) : NoValue)
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
    /// The job fields this sheet renders, joined so <see cref="Hooks.UseJobUpdatesExtensions.UseJobUpdates" />
    /// can re-render only when one of them actually changes.
    /// </summary>
    internal static string BuildSignature(JobItem job) => string.Create(CultureInfo.InvariantCulture,
        $"{job.Status};{job.Model};{job.Provider};{job.Type};{job.DurationSeconds};{job.Cost};{job.CostSource};{job.Tokens};{job.InputTokens};{job.OutputTokens};{job.CacheReadTokens};{job.CacheWriteTokens};{job.ReasoningTokens}");

    /// <summary>
    /// One row of the breakdown, in raw (unformatted) form so the footer totals can be summed
    /// before anything is stringified.
    /// </summary>
    internal readonly record struct Bucket(string Kind, int Tokens, decimal? Rate, bool CountsTowardTotal);

    internal static List<Bucket> BuildBuckets(JobItem job, ModelPricing? pricing)
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
        if (job.Tokens is > 0) totals.Add($"{Count(job.Tokens.Value)} tokens (input + output)");
        if (job.Cost is not null) totals.Add(Usd(job.Cost.Value));

        return Text.Block(string.Join(" · ", totals));
    }

    /// <summary>
    /// States plainly whether the displayed cost came from the agent's own report or from the rates
    /// in the table, and surfaces the gap when the two disagree.
    /// </summary>
    internal static string BuildReconciliation(JobItem job, decimal? computedCost)
    {
        if (job.Cost is null)
            return "No cost recorded for this job; the figures above are token counts only.";

        var charged = Usd(job.Cost.Value);

        if (job.CostSource == JobCostSources.Computed)
            return $"Charged {charged}, computed from the rates above (the agent did not report a cost).";

        if (job.CostSource == JobCostSources.Agent)
        {
            var text = $"Charged {charged} as reported by the {job.Provider} CLI. The per-token rates "
                       + "above are Tendril's reference prices and were not used for this figure.";

            if (computedCost.HasValue && Math.Abs(computedCost.Value - job.Cost.Value) > 0.01m)
                text += $" Those rates would give {Usd(computedCost.Value)}.";

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
    /// Maps a "Static catalog (claude)" source label back to the file the rates are written in.
    /// Returns null for any other source (e.g. a models.dev URL), which is already self-describing.
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

    private sealed record UsageRow
    {
        public required string Kind { get; init; }
        public required string Tokens { get; init; }
        public required string RatePerMillion { get; init; }
        public required string Cost { get; init; }
    }
}
