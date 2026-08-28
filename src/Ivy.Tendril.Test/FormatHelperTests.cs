using System.Globalization;
using Ivy.Tendril.Helpers;
using Xunit;

namespace Ivy.Tendril.Test;

/// <summary>
/// The app sets no <c>InvariantGlobalization</c> and never overrides the thread culture, so a
/// machine with a comma decimal separator would otherwise render a cost as "$1,2500" — a dollar
/// sign against a European decimal mark. These run on their own thread so the culture switch cannot
/// leak into another test running in parallel.
/// </summary>
public class FormatHelperTests
{
    private const string CommaDecimalCulture = "sv-SE";

    private static T InCulture<T>(string culture, Func<T> body)
    {
        T result = default!;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                result = body();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.Start();
        thread.Join();

        if (failure is not null) throw failure;
        return result;
    }

    [Fact]
    public void FormatCost_CommaDecimalCulture_StillUsesAPeriod()
    {
        Assert.Equal("$1.25", InCulture(CommaDecimalCulture, () => FormatHelper.FormatCost(1.25m)));
    }

    [Fact]
    public void FormatCost_FourDecimals_ForPerJobFigures()
    {
        Assert.Equal("$1.2500", InCulture(CommaDecimalCulture, () => FormatHelper.FormatCost(1.25m, decimals: 4)));
    }

    [Fact]
    public void FormatCost_DefaultsToTwoDecimals()
    {
        Assert.Equal("$0.00", FormatHelper.FormatCost(0m));
        Assert.Equal("$12.45", FormatHelper.FormatCost(12.449m));
    }

    [Fact]
    public void FormatCount_CommaDecimalCulture_GroupsWithCommas()
    {
        Assert.Equal("1,234,567", InCulture(CommaDecimalCulture, () => FormatHelper.FormatCount(1_234_567)));
    }

    [Fact]
    public void FormatTokens_CommaDecimalCulture_StillUsesAPeriod()
    {
        // Abbreviated counts sit next to the costs in the same Jobs table; "1,5M" beside "$1.25"
        // would be two different decimal conventions in adjacent columns.
        Assert.Equal("1.5M", InCulture(CommaDecimalCulture, () => FormatHelper.FormatTokens(1_500_000)));
    }

    [Fact]
    public void FormatTokens_KeepsThresholds()
    {
        Assert.Equal("999", FormatHelper.FormatTokens(999));
        Assert.Equal("1K", FormatHelper.FormatTokens(1_000));
        Assert.Equal("1.0M", FormatHelper.FormatTokens(1_000_000));
    }

    // Shared by the plan details row and the job cost sheet, which read the profile from different
    // places (the plan's yaml and the job's launch record) but must label it the same way.
    [Theory]
    [InlineData("deep", "Deep")]
    [InlineData("balanced", "Balanced")]
    [InlineData("Deep", "Deep")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void FormatExecutionProfile(string? profile, string? expected)
    {
        Assert.Equal(expected, FormatHelper.FormatExecutionProfile(profile));
    }
}
