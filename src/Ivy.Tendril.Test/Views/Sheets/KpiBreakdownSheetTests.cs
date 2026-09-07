using Ivy.Tendril.Apps.Views.Sheets;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Test.Views.Sheets;

public class KpiBreakdownSheetTests
{
    private readonly DateTime _today = new(2026, 8, 31);
    private readonly DashboardModels _stats = new(10, 2, 1, 1, 5, 1, 24.50m, [], []);
    private readonly DashboardActivityStats _activity;
    private readonly List<(DateOnly Date, int Count)> _prDays;
    private readonly FakePlanReaderService _fakeService;

    public KpiBreakdownSheetTests()
    {
        var dailyCosts = new List<DashboardDailyCost>
        {
            new(new DateOnly(2026, 8, 31), 25.00m, 2500),
            new(new DateOnly(2026, 8, 25), 15.50m, 1500),
            new(new DateOnly(2026, 8, 20), 10.00m, 1000)
        };

        var months = new List<DashboardMonthStats>
        {
            new(2026, 3, 5, 2, 150m, 15000),
            new(2026, 4, 8, 4, 250m, 25000),
            new(2026, 5, 10, 6, 350m, 35000),
            new(2026, 6, 12, 8, 450m, 45000),
            new(2026, 7, 15, 10, 550m, 55000),
            new(2026, 8, 3, 1, 100m, 10000) // In-flight month
        };

        _activity = new DashboardActivityStats(months, 20.00m, dailyCosts);

        _prDays =
        [
            (new DateOnly(2026, 8, 30), 2),
            (new DateOnly(2026, 8, 25), 3),
            (new DateOnly(2026, 8, 10), 1),
            (new DateOnly(2026, 7, 25), 4),
            (new DateOnly(2026, 7, 15), 2)
        ];

        _fakeService = new FakePlanReaderService
        {
            RecentMergedPrsToReturn =
            [
                new RecentMergedPrDto("https://github.com/org/repo/pull/1", 42, "Fix login", "/repos/app", DateTime.UtcNow),
                new RecentMergedPrDto("https://github.com/org/repo/pull/2", 43, "Add dark mode", "/repos/app", DateTime.UtcNow.AddDays(-1))
            ],
            RecentPlanCostsToReturn =
            [
                new RecentPlanCostDto(42, "Fix login", "Completed", DateTime.UtcNow.AddDays(-2), 12.50m, 15000),
                new RecentPlanCostDto(43, "Add dark mode", "Review", DateTime.UtcNow.AddDays(-1), null, 8000)
            ]
        };
    }

    [Fact]
    public void KpiBreakdownSheet_RendersDailyPrsBreakdown()
    {
        var sheet = new KpiBreakdownSheet("dailyPrs", _stats, _activity, _prDays, _today, _fakeService);
        var result = sheet.Build();

        Assert.NotNull(result);
    }

    [Fact]
    public void KpiBreakdownSheet_RendersAvgCostMonthBreakdown()
    {
        var sheet = new KpiBreakdownSheet("avgCostMonth", _stats, _activity, _prDays, _today, _fakeService);
        var result = sheet.Build();

        Assert.NotNull(result);
    }

    [Fact]
    public void KpiBreakdownSheet_RendersForecastMonthBreakdown()
    {
        var sheet = new KpiBreakdownSheet("forecastMonth", _stats, _activity, _prDays, _today, _fakeService);
        var result = sheet.Build();

        Assert.NotNull(result);
    }

    [Fact]
    public void KpiBreakdownSheet_RendersAvgCostPlanBreakdown()
    {
        var sheet = new KpiBreakdownSheet("avgCostPlan", _stats, _activity, _prDays, _today, _fakeService);
        var result = sheet.Build();

        Assert.NotNull(result);
    }

    [Fact]
    public void KpiBreakdownSheet_RendersUnknownKeyGracefully()
    {
        var sheet = new KpiBreakdownSheet("unknownMetricKey", _stats, _activity, _prDays, _today, _fakeService);
        var result = sheet.Build();

        Assert.NotNull(result);
    }

    [Fact]
    public void KpiBreakdownSheet_HandlesEmptyDataGracefully()
    {
        var emptyStats = new DashboardModels(0, 0, 0, 0, 0, 0, 0m, [], []);
        var emptyActivity = new DashboardActivityStats([], 0m);
        var emptyService = new FakePlanReaderService();

        var dailyPrsSheet = new KpiBreakdownSheet("dailyPrs", emptyStats, emptyActivity, [], _today, emptyService);
        var avgCostMonthSheet = new KpiBreakdownSheet("avgCostMonth", emptyStats, emptyActivity, [], _today, emptyService);
        var forecastSheet = new KpiBreakdownSheet("forecastMonth", emptyStats, emptyActivity, [], _today, emptyService);
        var avgCostPlanSheet = new KpiBreakdownSheet("avgCostPlan", emptyStats, emptyActivity, [], _today, emptyService);

        Assert.NotNull(dailyPrsSheet.Build());
        Assert.NotNull(avgCostMonthSheet.Build());
        Assert.NotNull(forecastSheet.Build());
        Assert.NotNull(avgCostPlanSheet.Build());
    }
}
