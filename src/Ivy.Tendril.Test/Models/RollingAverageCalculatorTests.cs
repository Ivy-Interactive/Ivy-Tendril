using Ivy.Tendril.Models;

namespace Ivy.Tendril.Test.Models;

public class RollingAverageCalculatorTests
{
    private static readonly DateOnly Day1 = new(2026, 1, 1);

    /// <summary><paramref name="count" /> ascending days, starting <paramref name="offset" /> days after Day 1.</summary>
    private static List<DateOnly> Days(int count, int offset = 0) =>
        Enumerable.Range(offset, count).Select(i => Day1.AddDays(i)).ToList();

    /// <summary>1 on Day 1, 2 on Day 2, and so on. 0 before Day 1.</summary>
    private static double Ramp(DateOnly date)
    {
        var index = date.DayNumber - Day1.DayNumber;
        return index >= 0 ? index + 1 : 0;
    }

    [Fact]
    public void Compute_VaryingValues_ProduceANonConstantSeries()
    {
        // The regression this calculator exists for: the line it replaced was one horizontal constant,
        // and a rolling mean that came out flat (or came out as the whole period's average) would be the
        // same non-information under a new name.
        var dates = Days(28);
        // Period 11 against a window of 7, so no window repeats the one before it.
        double Sawtooth(DateOnly date) => (date.DayNumber % 11) * 10;

        var rolling = RollingAverageCalculator.Compute(dates, Sawtooth, Day1);

        var values = rolling.Skip(RollingAverageCalculator.WindowDays - 1).Select(v => v!.Value).ToList();
        Assert.True(values.Distinct().Count() > 1, "the rolling series is flat");

        var wholeRangeMean = dates.Select(Sawtooth).Average();
        Assert.DoesNotContain(wholeRangeMean, values);
    }

    [Fact]
    public void Compute_AveragesTheDayAndTheSixBeforeIt()
    {
        // Values 1..14 by day: day 7 averages 1..7 and day 14 averages 8..14.
        var rolling = RollingAverageCalculator.Compute(Days(14), Ramp, Day1);

        Assert.Equal(14, rolling.Count);
        Assert.Equal(4d, rolling[6]);
        Assert.Equal(11d, rolling[13]);
    }

    [Fact]
    public void Compute_DividesByTheWindowSoAQuietDayCounts()
    {
        // Six days at 7 and one at nothing. Dividing by the days that had data would report 7, which is
        // the whole point of the zero fill: a day that cost nothing pulls the average down.
        var quietDay = Day1.AddDays(3);
        double ValueAt(DateOnly date) => date == quietDay ? 0 : 7;

        var rolling = RollingAverageCalculator.Compute(Days(1, 6), ValueAt, Day1);

        Assert.Equal(6d, Assert.Single(rolling));
    }

    [Fact]
    public void Compute_ReadsTheSixDaysBeforeTheDisplayedRange()
    {
        // Displayed range starts at day 11, so its first mean has to cover days 5..11 — none of which
        // except day 11 is in the range being plotted.
        var rolling = RollingAverageCalculator.Compute(Days(3, 10), Ramp, Day1);

        Assert.Equal(8d, rolling[0]);
        Assert.Equal(9d, rolling[1]);
        Assert.Equal(10d, rolling[2]);
    }

    [Fact]
    public void Compute_IsNullUntilTheWindowClearsTheDataStart()
    {
        // Records begin on day 4, so day 10 is the first date whose window (days 4..10) sits entirely
        // inside recorded history.
        var dataStart = Day1.AddDays(3);

        var rolling = RollingAverageCalculator.Compute(Days(12), Ramp, dataStart);

        Assert.All(rolling.Take(9), value => Assert.Null(value));
        Assert.NotNull(rolling[9]);
        Assert.Equal(7d, rolling[9]);
        Assert.NotNull(rolling[11]);
    }

    [Fact]
    public void Compute_WithoutADataStart_IsAllNull()
    {
        var rolling = RollingAverageCalculator.Compute(Days(20), Ramp, null);

        Assert.Equal(20, rolling.Count);
        Assert.All(rolling, value => Assert.Null(value));
    }

    [Fact]
    public void Compute_WithoutDates_IsEmpty()
    {
        Assert.Empty(RollingAverageCalculator.Compute([], Ramp, Day1));
    }

    [Fact]
    public void Compute_AllZeroWindow_IsZeroNotNull()
    {
        // A recorded week of no activity averages 0. Null would say "we have no idea", which is a
        // different and worse claim.
        var rolling = RollingAverageCalculator.Compute(Days(10), _ => 0d, Day1);

        Assert.All(rolling.Skip(RollingAverageCalculator.WindowDays - 1), value => Assert.Equal(0d, value));
    }
}
