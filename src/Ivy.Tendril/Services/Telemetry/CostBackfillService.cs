using System.Globalization;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Apps.Jobs;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Jobs;
using Ivy.Tendril.Services.Plans;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services.Telemetry;

/// <summary>
/// Repairs jobs whose cost was recorded as nothing when it should have been an estimate. Before the
/// estimated tier existed, a subscription run (Claude Max reports tokens and no charge) landed in the
/// database as a NULL or a 0.0, so months of real spend read as free. The token counts survived, and
/// with the price list they are enough to reconstruct the figure.
/// <para>
/// Works off the database rather than <c>JobService</c> memory: <c>LoadHistoricalJobs</c> holds
/// <c>GetRecentJobs()</c>, 100 jobs, while <c>PurgeOldJobs</c> keeps 500, so most of the jobs worth
/// repairing are only on disk. Jobs already purged keep whatever their <c>costs.csv</c> says; a model
/// name is not worth reconstructing from log files.
/// </para>
/// </summary>
public sealed class CostBackfillService(
    IPlanDatabaseService database,
    IModelPricingProvider pricingProvider,
    ILogger<CostBackfillService> logger,
    JobService? jobService = null) : IStartable, IDisposable
{
    // Comfortably after ModelPricingWarmupService's 15 seconds, so the first pass prices against
    // models.dev rather than the hardcoded catalogs where the two disagree.
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(60);

    /// <summary>Matches <c>PurgeOldJobs</c>, which is the real bound on how many jobs exist.</summary>
    internal const int JobLimit = 500;

    private Timer? _timer;

    /// <summary>
    ///     How <see cref="Run" /> decides it is not the master. A seam rather than a direct read
    ///     because the environment is process wide: a test flipping <c>TENDRIL_NOT_MASTER</c> to
    ///     exercise the guard also flips it for whatever <c>JobService</c> is reconciling restored
    ///     jobs on another thread at that moment, which is a real, observed flake.
    /// </summary>
    internal Func<bool> IsNotMaster { get; init; } =
        static () => Environment.GetEnvironmentVariable("TENDRIL_NOT_MASTER") == "1";

    public void Start()
    {
        // A single pass: after it, every candidate has either been filled or is unfillable, so
        // repeating would only re-read the same rows. New jobs are costed by JobCompletionHandler.
        _timer = new Timer(_ => Run(), null, InitialDelay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// One pass. Never throws: a background exception is a far worse outcome than a cost that stays
    /// blank, and every step here is best effort by nature.
    /// </summary>
    internal void Run()
    {
        // Only the master writes. A second instance filling the same rows would be harmless (the
        // arithmetic is deterministic) but it would also rewrite the same costs.csv concurrently.
        if (IsNotMaster())
        {
            logger.LogDebug("Skipping cost backfill (TENDRIL_NOT_MASTER=1)");
            return;
        }

        try
        {
            int filled = 0, unpriced = 0, failed = 0;

            foreach (var job in database.GetRecentJobs(JobLimit).Where(IsCandidate))
            {
                try
                {
                    var estimate = Estimate(job);
                    if (estimate is not { } cost)
                    {
                        unpriced++;
                        continue;
                    }

                    job.Cost = cost;
                    job.CostSource = JobCostSources.Estimated;
                    database.UpsertJob(job);
                    jobService?.ApplyBackfilledCost(job.Id, cost, JobCostSources.Estimated);
                    UpdateCostsCsv(job, cost);
                    filled++;
                }
                catch (Exception ex)
                {
                    failed++;
                    logger.LogDebug(ex, "Failed to backfill cost for job {JobId}", job.Id);
                }
            }

            if (filled > 0 || unpriced > 0 || failed > 0)
                logger.LogInformation(
                    "Cost backfill: {Filled} estimated, {Unpriced} skipped for unknown model, {Failed} failed",
                    filled, unpriced, failed);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cost backfill pass failed; costs left as they are");
        }
    }

    /// <summary>
    /// A job worth repairing: no cost anyone charged, a model to price against, and tokens to price.
    /// <para>
    /// The 0 case is deliberate; it is what the pre-migration writer stored for an unpriceable run,
    /// and telling it apart from a genuine free run is not possible from the row. A genuine zero
    /// re-derives to zero anyway, so treating them alike costs nothing. An <c>agent</c> source is
    /// never touched whatever its value: that figure came from a bill.
    /// </para>
    /// </summary>
    internal static bool IsCandidate(JobItem job) =>
        job.Cost is null or 0m
        && job.CostSource != JobCostSources.Agent
        && !string.IsNullOrWhiteSpace(job.Model)
        && (job.InputTokens is > 0 || job.OutputTokens is > 0
            || job.CacheReadTokens is > 0 || job.CacheWriteTokens is > 0);

    /// <summary>
    /// The same buckets-times-rates path <c>JobCompletionHandler</c> uses, so a backfilled figure and
    /// a freshly estimated one agree. Null when the model has no price list entry: an unknown model
    /// stays unknown rather than becoming a zero.
    /// </summary>
    private decimal? Estimate(JobItem job)
    {
        var pricing = pricingProvider.GetPricing(job.Model!);
        if (pricing is null) return null;

        return JobCostModelBuilder.ComputeCost(JobCostModelBuilder.BuildBuckets(job, pricing));
    }

    /// <summary>
    /// Writes the estimate into the plan folder's <c>costs.csv</c> as well. Not optional:
    /// <c>SyncPlanCosts</c> re-reads that file and <c>UpsertCosts</c> deletes and re-inserts every
    /// <c>Costs</c> row for the plan, so a database-only repair is undone by the next sync.
    /// <para>
    /// Rewrites a row only when exactly one row is both unpriced and this job's promptware. Several
    /// candidates means the file cannot say which run was this job, and guessing would move money
    /// onto the wrong row.
    /// </para>
    /// </summary>
    private void UpdateCostsCsv(JobItem job, decimal cost)
    {
        var planFolder = job.TypedArgs?.PlanFolder;
        if (string.IsNullOrWhiteSpace(planFolder)) return;

        var csvPath = Path.Combine(planFolder, "costs.csv");
        if (!File.Exists(csvPath)) return;

        var lines = FileHelper.ReadAllLines(csvPath);
        var matches = new List<int>();
        for (var i = 1; i < lines.Length; i++)
        {
            var parts = lines[i].Split(',');
            if (parts.Length < 3) continue;
            if (!string.Equals(parts[0].Trim(), job.Type, StringComparison.OrdinalIgnoreCase)) continue;
            if (!IsUnpriced(parts[2])) continue;
            matches.Add(i);
        }

        if (matches.Count != 1) return;

        var row = lines[matches[0]].Split(',');
        row[2] = cost.ToString("F4", CultureInfo.InvariantCulture);
        // A pre-v2 file has no Model column; widen the row rather than lose what the tokens went on.
        if (row.Length > 3) row[3] = job.Model ?? "";
        else row = [.. row, job.Model ?? ""];
        lines[matches[0]] = string.Join(",", row);

        FileHelper.WriteAllText(csvPath, string.Join("\n", lines) + "\n");
    }

    /// <summary>
    /// A cost field nothing was ever charged against: empty (v2's unknown) or a zero however it was
    /// formatted. A row carrying any other figure is left alone.
    /// </summary>
    private static bool IsUnpriced(string field)
    {
        var trimmed = field.Trim();
        return trimmed.Length == 0
               || (decimal.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
                   && value == 0m);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
