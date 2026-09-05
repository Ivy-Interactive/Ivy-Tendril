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
///     A 30 day daily spend series, for projecting the month. Null when the caller did not ask for
///     one, which is what every test fake supplies; the forecast then renders its no data state.
/// </param>
public record DashboardActivityStats(
    List<DashboardMonthStats> Months,
    decimal PrevWeekAvgCostPerPlan,
    List<DashboardDailyCost>? DailyCosts = null
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
