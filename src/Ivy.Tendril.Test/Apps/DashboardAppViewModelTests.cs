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

        Assert.Equal(4, kpis.Count);
        Assert.Equal("Avg Daily PR count", kpis[0].Label);
        Assert.Equal("1", kpis[0].Value);
        Assert.Equal("+100%", kpis[0].Delta);
        Assert.Equal("Avg Cost/Month", kpis[1].Label);
        Assert.Equal("Avg Tokens/Month", kpis[2].Label);
        Assert.Equal("Avg Cost/Plan", kpis[3].Label);
    }
}
