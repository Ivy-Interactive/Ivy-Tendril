namespace Ivy.Tendril.Models;

/// <summary>
///     A trailing mean over calendar days. Pure like <see cref="CostForecastCalculator" />: no clock of
///     its own and no formatting, so the arithmetic can be tested at fixed dates and the caller owns how
///     it reads.
/// </summary>
public static class RollingAverageCalculator
{
    /// <summary>Days in one window: the date itself plus the six calendar days before it.</summary>
    public const int WindowDays = 7;

    /// <summary>
    ///     Mean of each date and up to six calendar days before it. For history under seven days,
    ///     computes an expanding average from dataStart. Null where date is before dataStart or
    ///     when dataStart is null.
    /// </summary>
    /// <param name="dates">The displayed days. One entry out per entry in.</param>
    /// <param name="valueAt">
    ///     That day's value, zero-filled by the caller so a recorded day with no activity contributes 0.
    ///     Called for the six leading days too, which sit outside <paramref name="dates" />.
    /// </param>
    /// <param name="dataStart">
    ///     The earliest day records exist for. Null means there are none, and every entry is null.
    /// </param>
    public static List<double?> Compute(
        IReadOnlyList<DateOnly> dates,
        Func<DateOnly, double> valueAt,
        DateOnly? dataStart)
    {
        var result = new List<double?>(dates.Count);

        foreach (var date in dates)
        {
            if (dataStart is null || date < dataStart.Value)
            {
                result.Add(null);
                continue;
            }

            var windowStart = date.AddDays(-(WindowDays - 1));
            var effectiveStart = windowStart < dataStart.Value ? dataStart.Value : windowStart;
            var dayCount = date.DayNumber - effectiveStart.DayNumber + 1;

            var sum = 0d;
            for (var offset = 0; offset < dayCount; offset++)
                sum += valueAt(effectiveStart.AddDays(offset));

            result.Add(sum / dayCount);
        }

        return result;
    }
}
