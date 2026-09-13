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
