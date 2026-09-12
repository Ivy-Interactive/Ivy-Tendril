using Ivy;
using Ivy.Core;
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
    public void KpiBreakdownSheet_RendersForecastMonthBreakdown_WithSubsidizedAnalysis()
    {
        var dailyCosts = new List<DashboardDailyCost>
        {
            new(new DateOnly(2026, 8, 31), 40.00m, 4000, ApiCost: 10m, ApiTokens: 1000, SubsidizedCost: 30m, SubsidizedTokens: 3000),
            new(new DateOnly(2026, 8, 25), 20.00m, 2000, ApiCost: 0m, ApiTokens: 0, SubsidizedCost: 20m, SubsidizedTokens: 2000)
        };
        var activity = new DashboardActivityStats([], 0m, dailyCosts);
        var sheet = new KpiBreakdownSheet("forecastMonth", _stats, activity, _prDays, _today, _fakeService);
        var result = sheet.Build();

        Assert.NotNull(result);
        var tableContent = ExtractTableContent(result);
        Assert.NotNull(tableContent);
        Assert.StartsWith("DataTableBuilder", tableContent.GetType().Name);
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

    [Fact]
    public void KpiBreakdownSheet_PopulatedData_BuildsDataTables()
    {
        var dailyPrsSheet = new KpiBreakdownSheet("dailyPrs", _stats, _activity, _prDays, _today, _fakeService);
        var avgCostMonthSheet = new KpiBreakdownSheet("avgCostMonth", _stats, _activity, _prDays, _today, _fakeService);
        var forecastSheet = new KpiBreakdownSheet("forecastMonth", _stats, _activity, _prDays, _today, _fakeService);
        var avgCostPlanSheet = new KpiBreakdownSheet("avgCostPlan", _stats, _activity, _prDays, _today, _fakeService);

        var dailyPrsTable = ExtractTableContent(dailyPrsSheet.Build());
        var avgCostMonthTable = ExtractTableContent(avgCostMonthSheet.Build());
        var forecastTable = ExtractTableContent(forecastSheet.Build());
        var avgCostPlanTable = ExtractTableContent(avgCostPlanSheet.Build());

        Assert.NotNull(dailyPrsTable);
        Assert.StartsWith("DataTableBuilder", dailyPrsTable.GetType().Name);

        Assert.NotNull(avgCostMonthTable);
        Assert.StartsWith("DataTableBuilder", avgCostMonthTable.GetType().Name);

        Assert.NotNull(forecastTable);
        Assert.StartsWith("DataTableBuilder", forecastTable.GetType().Name);

        Assert.NotNull(avgCostPlanTable);
        Assert.StartsWith("DataTableBuilder", avgCostPlanTable.GetType().Name);
    }

    [Fact]
    public void KpiBreakdownSheet_EmptyData_ProducesCalloutPlaceholders()
    {
        var emptyStats = new DashboardModels(0, 0, 0, 0, 0, 0, 0m, [], []);
        var emptyActivity = new DashboardActivityStats([], 0m);
        var emptyService = new FakePlanReaderService();

        var dailyPrsSheet = new KpiBreakdownSheet("dailyPrs", emptyStats, emptyActivity, [], _today, emptyService);
        var forecastSheet = new KpiBreakdownSheet("forecastMonth", emptyStats, emptyActivity, [], _today, emptyService);
        var avgCostPlanSheet = new KpiBreakdownSheet("avgCostPlan", emptyStats, emptyActivity, [], _today, emptyService);

        var dailyPrsContent = ExtractTableContent(dailyPrsSheet.Build());
        var forecastContent = ExtractTableContent(forecastSheet.Build());
        var avgCostPlanContent = ExtractTableContent(avgCostPlanSheet.Build());

        Assert.IsType<Callout>(dailyPrsContent);
        Assert.IsType<Callout>(forecastContent);
        Assert.IsType<Callout>(avgCostPlanContent);
    }

    [Fact]
    public void KpiBreakdownSheet_MoneyMetrics_RenderAgentBreakdownWithData()
    {
        _fakeService.AgentCostsToReturn =
        [
            new DashboardAgentCost("claude", 50.00m, 50000, 3),
            new DashboardAgentCost("codex", 25.00m, 25000, 2),
            new DashboardAgentCost("Unknown", 10.00m, 10000, 1)
        ];

        var avgCostMonthSheet = new KpiBreakdownSheet("avgCostMonth", _stats, _activity, _prDays, _today, _fakeService);
        var forecastSheet = new KpiBreakdownSheet("forecastMonth", _stats, _activity, _prDays, _today, _fakeService);
        var avgCostPlanSheet = new KpiBreakdownSheet("avgCostPlan", _stats, _activity, _prDays, _today, _fakeService);

        var avgCostMonthSection = ExtractAgentSection(avgCostMonthSheet.Build());
        var forecastSection = ExtractAgentSection(forecastSheet.Build());
        var avgCostPlanSection = ExtractAgentSection(avgCostPlanSheet.Build());

        Assert.NotNull(avgCostMonthSection);
        Assert.StartsWith("DataTableBuilder", avgCostMonthSection.GetType().Name);
        Assert.NotNull(forecastSection);
        Assert.StartsWith("DataTableBuilder", forecastSection.GetType().Name);
        Assert.NotNull(avgCostPlanSection);
        Assert.StartsWith("DataTableBuilder", avgCostPlanSection.GetType().Name);
    }

    [Fact]
    public void KpiBreakdownSheet_DailyPrs_DoesNotRenderAgentBreakdown()
    {
        _fakeService.AgentCostsToReturn =
        [
            new DashboardAgentCost("claude", 50.00m, 50000, 3)
        ];

        var dailyPrsSheet = new KpiBreakdownSheet("dailyPrs", _stats, _activity, _prDays, _today, _fakeService);
        var result = dailyPrsSheet.Build();

        // dailyPrs should not have an agent section (only 3 children, not 4)
        var layout = Assert.IsAssignableFrom<LayoutView>(result);
        var stack = Assert.IsAssignableFrom<IWidget>(layout.Build());
        Assert.Equal(3, stack.Children.Count);
    }

    [Fact]
    public void KpiBreakdownSheet_MoneyMetrics_EmptyAgentData_ProducesCallout()
    {
        _fakeService.AgentCostsToReturn = [];

        var avgCostMonthSheet = new KpiBreakdownSheet("avgCostMonth", _stats, _activity, _prDays, _today, _fakeService);
        var forecastSheet = new KpiBreakdownSheet("forecastMonth", _stats, _activity, _prDays, _today, _fakeService);
        var avgCostPlanSheet = new KpiBreakdownSheet("avgCostPlan", _stats, _activity, _prDays, _today, _fakeService);

        var avgCostMonthSection = ExtractAgentSection(avgCostMonthSheet.Build());
        var forecastSection = ExtractAgentSection(forecastSheet.Build());
        var avgCostPlanSection = ExtractAgentSection(avgCostPlanSheet.Build());

        Assert.IsType<Callout>(avgCostMonthSection);
        Assert.IsType<Callout>(forecastSection);
        Assert.IsType<Callout>(avgCostPlanSection);
    }

    private static object ExtractTableContent(object buildResult)
    {
        var layout = Assert.IsAssignableFrom<LayoutView>(buildResult);
        var stack = Assert.IsAssignableFrom<IWidget>(layout.Build());
        var innerLayout = Assert.IsAssignableFrom<LayoutView>(stack.Children[2]);
        var innerStack = Assert.IsAssignableFrom<IWidget>(innerLayout.Build());
        return innerStack.Children[1];
    }

    private static object ExtractAgentSection(object buildResult)
    {
        var layout = Assert.IsAssignableFrom<LayoutView>(buildResult);
        var stack = Assert.IsAssignableFrom<IWidget>(layout.Build());
        // Agent section is the 4th child (index 3) for money metrics
        var agentLayout = Assert.IsAssignableFrom<LayoutView>(stack.Children[3]);
        var agentStack = Assert.IsAssignableFrom<IWidget>(agentLayout.Build());
        // Return the second child (index 1), which is either the DataTable or Callout
        return agentStack.Children[1];
    }
}
