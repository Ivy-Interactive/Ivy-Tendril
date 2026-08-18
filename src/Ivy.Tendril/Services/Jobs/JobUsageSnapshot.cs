using Ivy.Tendril.Models;

namespace Ivy.Tendril.Services.Jobs;

/// <summary>
/// Provenance values for <see cref="JobItem.CostSource"/>.
/// </summary>
public static class JobCostSources
{
    /// <summary>The agent CLI reported the charge itself; local price lists were not used.</summary>
    public const string Agent = "agent";

    /// <summary>The agent reported no cost, so it was computed from Tendril's model price list.</summary>
    public const string Computed = "computed";
}

/// <summary>
/// The reconciled usage for one job: the two headline numbers the Jobs table shows
/// (<see cref="Tokens"/>, <see cref="Cost"/>) plus the per-bucket breakdown behind them and the
/// provenance of the cost. Produced by <see cref="JobCompletionHandler.ResolveJobCost"/>.
/// </summary>
public sealed record JobUsageSnapshot
{
    /// <summary>Input + output tokens; excludes the cache buckets (see <see cref="JobItem.Tokens"/>).</summary>
    public int Tokens { get; init; }

    /// <summary>Null rather than 0 when neither source reported a positive cost, so the UI shows
    /// nothing instead of a misleading $0.0000.</summary>
    public decimal? Cost { get; init; }

    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public int? CacheReadTokens { get; init; }
    public int? CacheWriteTokens { get; init; }
    public int? ReasoningTokens { get; init; }
    public string? Model { get; init; }

    /// <summary>One of <see cref="JobCostSources"/>, or null when there is no cost.</summary>
    public string? CostSource { get; init; }

    /// <summary>
    /// Writes this snapshot onto a job. <see cref="Model"/> is only written when known, so a model
    /// already recorded at launch is never wiped by a usage report that omits it.
    /// </summary>
    public void ApplyTo(JobItem job)
    {
        job.Cost = Cost;
        job.Tokens = Tokens;
        job.InputTokens = InputTokens;
        job.OutputTokens = OutputTokens;
        job.CacheReadTokens = CacheReadTokens;
        job.CacheWriteTokens = CacheWriteTokens;
        job.ReasoningTokens = ReasoningTokens;
        job.CostSource = CostSource;
        if (Model is not null)
            job.Model = Model;
    }
}
