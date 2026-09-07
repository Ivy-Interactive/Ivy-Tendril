namespace Ivy.Tendril.Models;

public record DashboardModels(
    int TotalCount,
    int DraftCount,
    int InProgressCount,
    int ReviewCount,
    int CompletedCount,
    int FailedCount,
    decimal AvgCostPerPlan,
    List<DashboardDayStats> DailyStats,
    List<ProjectCount> ProjectCounts
);

public record DashboardDayStats(
    DateTime Date,
    int Created,
    int Completed,
    int PrsMerged,
    int Failed,
    decimal Cost,
    int Tokens
);

public record ProjectCount(string Project, int Count);

/// <param name="DailyCosts">
///     A daily spend series, for projecting the month and plotting the trend chart. Null when the
///     caller did not ask for one, which is what every test fake supplies; the forecast then renders
///     its no data state and the trend card is absent.
/// </param>
/// <param name="DailyDataStart">
///     The earliest day the daily series could hold a record for, clamped up to the start of the
///     retrieval window. A day on or after it with no rows genuinely cost nothing and created nothing;
///     a day before it is unknown, and averaging over it would report a figure nobody spent. Null when
///     there are no records at all.
/// </param>
public record DashboardActivityStats(
    List<DashboardMonthStats> Months,
    decimal PrevWeekAvgCostPerPlan,
    List<DashboardDailyCost>? DailyCosts = null,
    Dictionary<DateOnly, int>? DailyPlans = null,
    DateOnly? DailyDataStart = null
);

/// <summary>
///     One day's spend. Days with no <c>Costs</c> row are absent rather than present as zero, so a
///     reader of the series can tell "no activity" from "activity that cost nothing".
/// </summary>
public record DashboardDailyCost(DateOnly Date, decimal Cost, long Tokens);

public record DashboardMonthStats(
    int Year,
    int Month,
    int PlansCreated,
    int PrsMerged,
    decimal Cost,
    long Tokens
);

public record RecentMergedPrDto(
    string PrUrl,
    int PlanId,
    string PlanTitle,
    string? Repo,
    DateTime MergedDate
);

public record RecentPlanCostDto(
    int PlanId,
    string PlanTitle,
    string State,
    DateTime CreatedDate,
    decimal? Cost,
    long Tokens
);

