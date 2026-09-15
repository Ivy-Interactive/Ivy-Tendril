using Ivy.Tendril.Apps.Jobs;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Services.Plans;

public interface IPlanDatabaseService : IDisposable
{
    // Plan queries
    List<PlanFile> GetPlans(PlanStatus? statusFilter = null);
    PlanFile? GetPlanByFolder(string folderPath);
    PlanFile? GetPlanById(int planId);

    // Aggregates
    PlanReaderService.PlanCountSnapshot ComputePlanCounts();
    DashboardModels GetDashboardData(string? projectFilter);
    DashboardActivityStats GetActivityStats(int monthsBack = 24);
    List<(DateOnly Date, int Count)> GetCompletedPrsByDay(int days = 30);
    List<(DateOnly Date, int Count)> GetShippedFeaturesByDay(int days = 60) => [];
    List<RecentMergedPrDto> GetRecentMergedPrs(int limit = 50) => [];
    List<RecentPlanCostDto> GetRecentPlanCosts(int days = 7) => [];
    List<DashboardAgentCost> GetAgentCostBreakdown(int days) => [];

    // Costs and tokens
    decimal GetPlanTotalCost(int planId);
    int GetPlanTotalTokens(int planId);
    List<HourlyTokenBurn> GetHourlyTokenBurn(int days = 7, string? projectFilter = null);
    string? ResolveCostSource(int planId, string promptware, string? folderPath = null, string? folderName = null) => null;
    string? ResolveAgent(int planId, string promptware, string? folderPath = null, string? folderName = null) => null;

    // Recommendations
    List<Recommendation> GetRecommendations();
    int GetPendingRecommendationsCount();

    // Search
    List<PlanFile> SearchPlans(string query);
    void RebuildFtsIndex();

    // Immediate mutations (DB-first for UI responsiveness)
    void UpdatePlanState(int planId, PlanStatus state);
    void UpdatePlanContent(int planId, string latestRevisionContent, int revisionCount);
    void UpdateRecommendationState(int planId, string recommendationTitle, string newState, string? declineReason);

    // Sync operations (bulk, called by sync service)

    /// <summary>
    ///     Opens a batch: one write lock and one transaction held for the caller's whole loop instead of
    ///     one of each per plan. A full sync otherwise takes the write lock twice per plan, and every
    ///     acquisition is another chance for a UI read to queue behind it (#2571). Not nestable.
    /// </summary>
    /// <returns>A scope that commits when disposed; a no-op for an implementation with no connection.</returns>
    IDisposable BeginBatch() => NullBatch.Instance;

    void UpsertPlan(PlanFile plan);
    void DeletePlan(int planId);
    void UpsertCosts(int planId, List<CostEntry> costs);

    void UpsertRecommendations(int planId, string folderName, List<RecommendationYaml> recommendations, string project,
        string planTitle, DateTime updated, PlanStatus status);

    void BulkUpsertPlans(List<PlanFile> plans, bool forceOverwrite = false);
    HashSet<int> GetTerminalPlanIds();

    // Jobs
    void UpsertJob(JobItem job);
    List<JobItem> GetRecentJobs(int limit = 100);
    JobItem? GetJobById(string id);
    List<JobItem> GetJobsForPlan(string planFile);
    List<string> PurgeOldJobs(int keepCount = 500);
    void DeleteJob(string id);

    /// <summary>
    ///     Live rows sharing a conflict key, newest first, for the cross-instance half of the dedup
    ///     guard. The in-memory scan only sees jobs this process launched, and during the #2710 storm
    ///     3-4 instances shared one TENDRIL_HOME. Candidates, not verdicts: the caller decides whether a
    ///     row is really live, because a row left <c>Running</c> by a crashed instance must not lock its
    ///     scope out forever.
    /// </summary>
    List<JobConflictCandidate> FindLiveJobsByConflictKey(
        IReadOnlyCollection<string> jobTypes, string conflictKey, string excludeJobId) => [];

    // Machine wide job slots. maxConcurrentJobs used to be enforced by a per-process SemaphoreSlim, so
    // 30 became 120 across four instances and the OOM killer decided the real limit (#2710).

    /// <summary>
    ///     Takes a slot for <paramref name="jobId" /> if fewer than <paramref name="maxConcurrentJobs" />
    ///     are live machine wide. One immediate transaction covers reclaim, count and insert, so two
    ///     instances cannot both read <c>max - 1</c>. Idempotent per job id, so a retry after a partial
    ///     failure cannot double count.
    /// </summary>
    /// <param name="isPidAliveSince">
    ///     Whether a recorded pid is alive and started no later than the given instant, the lease's
    ///     <c>AcquiredAt</c>. Injected because staleness is a policy question the caller owns, and
    ///     reclaim has to happen inside the acquiring transaction to be race free.
    /// </param>
    /// <param name="liveCount">Slots live after this call, for the queued job's status message.</param>
    /// <remarks>Default admits everything: an implementation with no database cannot cap anything.</remarks>
    bool TryAcquireJobSlot(string jobId, int maxConcurrentJobs, int ownerPid, string machineName,
        TimeSpan leaseTtl, Func<int, DateTime, bool> isPidAliveSince, out int liveCount)
    {
        liveCount = 0;
        return true;
    }

    /// <summary>
    ///     Advances the heartbeat of every named lease this instance still owns, re-inserting any row
    ///     that has gone missing. A wrongly reclaimed lease means momentarily overshooting the cap by
    ///     one, which self corrects on completion; killing healthy work does not.
    /// </summary>
    /// <returns>Job ids whose lease had to be re-inserted, for the caller to log.</returns>
    List<string> RenewJobSlots(IReadOnlyCollection<string> jobIds, int ownerPid, string machineName) => [];

    /// <summary>Records the agent pid, so a detached agent outliving its instance keeps its slot.</summary>
    void SetJobSlotAgentPid(string jobId, int agentPid) { }

    void ReleaseJobSlot(string jobId) { }

    /// <summary>
    ///     Drops leases nothing is using: heartbeat older than <paramref name="ttl" /> with neither pid
    ///     alive, or an owning <c>Jobs</c> row already in a terminal state. Rows from another machine go
    ///     on TTL alone, since a pid there means nothing here.
    /// </summary>
    /// <returns>How many were reclaimed.</returns>
    int ReclaimStaleJobSlots(Func<int, DateTime, bool> isPidAliveSince, TimeSpan ttl, string machineName) => 0;

    /// <summary>Live lease count, for diagnostics and tests.</summary>
    int CountLiveJobSlots() => 0;

    // PR statuses
    Dictionary<string, PrInfo> GetAllPrStatuses();
    void UpsertPrStatus(string prUrl, string owner, string repo, string status, string branch, DateTime lastChecked);
    List<string> GetNonMergedPrUrls();

    // Diagnostics
    long GetDatabaseSize();
    DateTime GetLastSyncTime();
    void SetLastSyncTime(DateTime time);
}

/// <summary>
///     What <see cref="IPlanDatabaseService.BeginBatch" /> hands back when there is nothing to batch —
///     the in-memory fakes the tests use, which have neither a lock nor a transaction to hold.
/// </summary>
internal sealed class NullBatch : IDisposable
{
    internal static readonly NullBatch Instance = new();
    public void Dispose() { }
}

/// <summary>
///     A <c>Jobs</c> row that shares a conflict key with an incoming submission, with just enough to
///     judge whether it is really still live.
/// </summary>
/// <param name="ProcessId">The agent pid, when one was recorded. Null for a job that never launched.</param>
/// <param name="StartedAt">Null for a row that was still <c>Pending</c>, which counts as live.</param>
public record JobConflictCandidate(string Id, string Type, int? ProcessId, DateTime? StartedAt);

/// <summary>
///     One row of a plan folder's <c>costs.csv</c>, as synced into the <c>Costs</c> table.
/// </summary>
/// <param name="Cost">
///     Null when nothing could be priced: a subscription plan reports tokens but no charge, and the
///     aggregates have to skip that plan rather than average a zero in. Distinct from a genuine 0.
/// </param>
/// <param name="Model">
///     What the tokens went on, carried through the CSV because <c>PurgeOldJobs</c> drops the
///     <c>Jobs</c> row long before the plan folder is archived. Null for a pre v2 file.
/// </param>
public record CostEntry(string Promptware, int Tokens, decimal? Cost, DateTime? LogTimestamp, string? Model = null, string? CostSource = null, string? Agent = null);
