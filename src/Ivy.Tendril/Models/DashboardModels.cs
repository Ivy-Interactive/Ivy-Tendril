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
    List<ProjectCount> ProjectCounts,
    List<MonthlyTrendPoint>? MonthlyTrends = null,
    List<PrBarPoint>? PrBarStats = null
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

public record MonthlyTrendPoint(
    string Month,
    double ThisYearCost,
    double LastYearCost,
    int ThisYearPlans,
    int LastYearPlans
);

public record PrBarPoint(
    string Period,
    int PrCount,
    int CompletedCount,
    int DraftCount
);

public record ProjectCount(string Project, int Count);
