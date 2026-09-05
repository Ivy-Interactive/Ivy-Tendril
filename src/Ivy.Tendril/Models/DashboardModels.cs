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

public record DashboardActivityStats(
    List<DashboardMonthStats> Months,
    decimal PrevWeekAvgCostPerPlan
);

public record DashboardMonthStats(
    int Year,
    int Month,
    int PlansCreated,
    int PrsMerged,
    decimal Cost,
    long Tokens
);
