using Ivy.Tendril.Apps;
using Ivy.Tendril.Models;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Test.Apps;

public class DashboardAppViewModelTests
{
    [Theory]
    [InlineData(1, "1st")]
    [InlineData(2, "2nd")]
    [InlineData(3, "3rd")]
    [InlineData(4, "4th")]
    [InlineData(11, "11th")]
    [InlineData(12, "12th")]
    [InlineData(13, "13th")]
    [InlineData(21, "21st")]
    [InlineData(22, "22nd")]
    [InlineData(31, "31st")]
    public void Ordinal_FormatsDaySuffix(int day, string expected)
    {
        Assert.Equal(expected, DashboardApp.Ordinal(day));
    }

    [Fact]
    public void Kpi_FormatsPositiveDelta()
    {
        var kpi = DashboardApp.Kpi("Label", "54", 54m, 24.2m);

        Assert.Equal("+123%", kpi.Delta);
        Assert.Equal("up", kpi.Direction);
    }

    [Fact]
    public void Kpi_FormatsSmallNegativeDeltaWithDecimals()
    {
        var kpi = DashboardApp.Kpi("Label", "$0.98", 0.9799m, 0.98m);

        Assert.Equal("-0.01%", kpi.Delta);
        Assert.Equal("down", kpi.Direction);
    }

    [Fact]
    public void Kpi_OmitsDeltaWithoutBaseline()
    {
        var kpi = DashboardApp.Kpi("Label", "80,720", 80720m, 0m);

        Assert.Null(kpi.Delta);
        Assert.Null(kpi.Direction);
    }

    [Fact]
    public void BuildTrend_TakesTrailingTwelveMonths()
    {
        var months = new List<DashboardMonthStats>();
        for (var month = 1; month <= 12; month++)
            months.Add(new DashboardMonthStats(2025, month, month, 0, month * 100m, 0));
        for (var month = 1; month <= 8; month++)
            months.Add(new DashboardMonthStats(2026, month, month * 2, 0, month * 200m, 0));

        var trend = DashboardApp.BuildTrend(new DashboardActivityStats(months, 0));

        Assert.Equal(12, trend.Months.Count);
        Assert.Equal("Sep", trend.Months[0]);
        Assert.Equal("Aug", trend.Months[^1]);
        Assert.Equal(900, trend.Cost[0]);
        Assert.Equal(1600, trend.Cost[^1]);
        Assert.Equal(9, trend.Plans[0]);
        Assert.Equal(16, trend.Plans[^1]);
        Assert.Null(trend.PrevCost[0]);
        Assert.Null(trend.PrevCost[3]);
        Assert.Equal(100, trend.PrevCost[4]);
        Assert.Equal(800, trend.PrevCost[^1]);
        Assert.Equal(1, trend.PrevPlans[4]);
        Assert.Equal(8, trend.PrevPlans[^1]);
    }

    [Fact]
    public void BuildTrend_KeepsShortHistoryAsIs()
    {
        var months = new List<DashboardMonthStats>
        {
            new(2026, 7, 3, 0, 120m, 0),
            new(2026, 8, 5, 0, 250m, 0)
        };

        var trend = DashboardApp.BuildTrend(new DashboardActivityStats(months, 0));

        Assert.Equal(["Jul", "Aug"], trend.Months);
        Assert.Equal([120d, 250d], trend.Cost);
        Assert.Equal([3d, 5d], trend.Plans);
        Assert.Equal([null, null], trend.PrevCost);
        Assert.Equal([null, null], trend.PrevPlans);
    }

    [Fact]
    public void BuildActivityMonths_BucketsDaysIntoCalendarWeeks()
    {
        // June 2026: the 1st is a Monday, so weeks align exactly with rows.
        var firstMonth = new DateTime(2026, 6, 1);
        var prDays = new List<(DateOnly Date, int Count)>
        {
            (new DateOnly(2026, 6, 1), 2),
            (new DateOnly(2026, 6, 7), 3),
            (new DateOnly(2026, 6, 8), 5),
            (new DateOnly(2026, 6, 30), 1)
        };

        var months = DashboardApp.BuildActivityMonths(prDays, firstMonth);

        Assert.Equal(16, months.Count);
        var june = months[0];
        Assert.Equal("Jun", june.Label);
        Assert.Equal(5, june.Weeks.Count);
        Assert.Equal(5, june.Weeks[0]);
        Assert.Equal(5, june.Weeks[1]);
        Assert.Equal(0, june.Weeks[2]);
        Assert.Equal(1, june.Weeks[4]);
    }

    [Fact]
    public void BuildActivityMonths_OffsetsWeeksByFirstWeekday()
    {
        // August 2026: the 1st is a Saturday, so the first calendar week holds only two days.
        var firstMonth = new DateTime(2026, 8, 1);
        var prDays = new List<(DateOnly Date, int Count)>
        {
            (new DateOnly(2026, 8, 1), 4),
            (new DateOnly(2026, 8, 3), 7)
        };

        var months = DashboardApp.BuildActivityMonths(prDays, firstMonth);

        var august = months[0];
        Assert.Equal(4, august.Weeks[0]);
        Assert.Equal(7, august.Weeks[1]);
    }

    [Fact]
    public void BuildActiveJobs_KeepsOnlyActiveStatuses()
    {
        var jobs = new List<JobItem>
        {
            new() { Id = "job-1", Status = JobStatus.Running, PlanFile = "00001-a" },
            new() { Id = "job-2", Status = JobStatus.Completed, PlanFile = "00002-b" },
            new() { Id = "job-3", Status = JobStatus.Queued, PlanFile = "00003-c" },
            new() { Id = "job-4", Status = JobStatus.Failed, PlanFile = "00004-d" },
            new() { Id = "job-5", Status = JobStatus.Blocked, PlanFile = "00005-e" },
            new() { Id = "job-6", Status = JobStatus.Pending, PlanFile = "00006-f" },
            new() { Id = "job-7", Status = JobStatus.Stopped, PlanFile = "00007-g" }
        };

        var result = DashboardApp.BuildActiveJobs(jobs, new FakePlanReaderService());

        Assert.Equal(["job-1", "job-3", "job-5", "job-6"], result.Select(j => j.Id));
        Assert.Equal(["running", "queued", "blocked", "pending"], result.Select(j => j.Status));
    }

    [Fact]
    public void BuildActiveJobs_MapsPlanIdWithReportedFallback()
    {
        var jobs = new List<JobItem>
        {
            new() { Id = "job-1", Status = JobStatus.Running, PlanFile = "00042-fix-tests" },
            new() { Id = "job-2", Status = JobStatus.Running, PlanFile = "", ReportedPlanId = "00043" },
            new() { Id = "job-3", Status = JobStatus.Running, PlanFile = "" }
        };

        var result = DashboardApp.BuildActiveJobs(jobs, new FakePlanReaderService());

        Assert.Equal(["00042", "00043", ""], result.Select(j => j.PlanId));
    }

    [Fact]
    public void BuildActiveJobs_CapsListLength()
    {
        var jobs = Enumerable.Range(1, 12)
            .Select(i => new JobItem { Id = $"job-{i}", Status = JobStatus.Queued, PlanFile = "" })
            .ToList();

        var result = DashboardApp.BuildActiveJobs(jobs, new FakePlanReaderService());

        Assert.Equal(8, result.Count);
    }

    [Fact]
    public void BuildKpis_UsesThirtyDayPrWindows()
    {
        var today = new DateTime(2026, 8, 31);
        var prDays = new List<(DateOnly Date, int Count)>
        {
            (DateOnly.FromDateTime(today.AddDays(-5)), 30),
            (DateOnly.FromDateTime(today.AddDays(-40)), 15)
        };
        var stats = new DashboardModels(1, 0, 0, 0, 1, 0, 0m, [], []);
        var activity = new DashboardActivityStats([], 0);

        var kpis = DashboardApp.BuildKpis(stats, activity, prDays, today);

        Assert.Equal(5, kpis.Count);
        Assert.Equal("Avg Daily PR count", kpis[0].Label);
        Assert.Equal("1", kpis[0].Value);
        Assert.Equal("+100%", kpis[0].Delta);
        Assert.Equal("Avg Cost/Month", kpis[1].Label);
        // The projection sits next to the retrospective average it is read against.
        Assert.Equal("Forecast This Month", kpis[2].Label);
        Assert.Equal("Avg Tokens/Month", kpis[3].Label);
        Assert.Equal("Avg Cost/Plan", kpis[4].Label);
    }

    [Fact]
    public void BuildKpis_ForecastHintStatesBothDayCounts()
    {
        // August 2026 has 31 days. Ten days of history, three of which cost anything: the calendar
        // basis divides by 10, the activity basis by 3, and the hint has to name both.
        var today = new DateTime(2026, 8, 31);
        var dailyCosts = new List<DashboardDailyCost>
        {
            new(DateOnly.FromDateTime(today.AddDays(-9)), 10m, 1000),
            new(DateOnly.FromDateTime(today.AddDays(-5)), 20m, 2000),
            new(DateOnly.FromDateTime(today), 30m, 3000)
        };
        var stats = new DashboardModels(1, 0, 0, 0, 1, 0, 0m, [], []);
        var activity = new DashboardActivityStats([], 0, dailyCosts);

        var forecast = DashboardApp.BuildKpis(stats, activity, [], today)
            .Single(k => k.Label == "Forecast This Month");

        // 60 over 10 days times 31, rounded by FormatCost above 100.
        Assert.Equal("$186", forecast.Value);
        Assert.NotNull(forecast.Hint);
        Assert.Contains("10 days", forecast.Hint);
        Assert.Contains("3 active days", forecast.Hint);
        Assert.Contains("$620", forecast.Hint);
    }

    [Fact]
    public void BuildKpis_ForecastWithoutDailyCosts_RendersNoDataState()
    {
        // Null DailyCosts is what every fake supplies, and what GetActivityStats returned before this
        // series existed. It has to read as "nothing to project from", not as a confident $0.00.
        var stats = new DashboardModels(1, 0, 0, 0, 1, 0, 0m, [], []);
        var activity = new DashboardActivityStats([], 0);

        var forecast = DashboardApp.BuildKpis(stats, activity, [], new DateTime(2026, 8, 31))
            .Single(k => k.Label == "Forecast This Month");

        Assert.Equal("-", forecast.Value);
        Assert.Equal("No cost data in the last 30 days", forecast.Hint);
        Assert.Null(forecast.Delta);
    }
}
