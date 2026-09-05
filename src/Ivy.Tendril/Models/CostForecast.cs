namespace Ivy.Tendril.Models;

/// <summary>
///     A month's spend projected two ways, both reported rather than one picked. Neither is right on
///     its own: the calendar basis assumes the idle days keep coming, the activity basis assumes every
///     day is a working day, and for bursty usage the gap between them is the actual uncertainty.
/// </summary>
/// <param name="CalendarProjection">
///     Spend per calendar day, times the days in the month. The lower bound. Null when there is no
///     spend to project from, so a caller renders "no data" rather than a confident $0.00.
/// </param>
/// <param name="CalendarDays">Calendar days of history the projection divided by. 0 when there is none.</param>
/// <param name="ActivityProjection">
///     Spend per day that had spend, times the days in the month. The upper bound, and never below
///     <paramref name="CalendarProjection" /> since it divides by a subset of the same days.
/// </param>
/// <param name="ActivityDays">Days in the window that actually cost something. 0 when none did.</param>
/// <param name="TotalSpend">Sum of the supplied days, so the projections can be sanity checked.</param>
/// <param name="DaysInMonth">The month's length, which is why the same daily rate projects higher in March than February.</param>
public record CostForecast(
    decimal? CalendarProjection,
    int CalendarDays,
    decimal? ActivityProjection,
    int ActivityDays,
    decimal TotalSpend,
    int DaysInMonth);

/// <summary>
///     Projects a month's spend from a daily series. Pure on purpose: no clock of its own, no
///     formatting, so the arithmetic can be tested at a fixed date and <c>DashboardApp</c> owns how it
///     reads.
/// </summary>
public static class CostForecastCalculator
{
    /// <summary>Matches <c>DashboardRepository.DailyCostWindowDays</c>, and caps a longer history.</summary>
    private const int WindowDays = 30;

    /// <summary>
    ///     Projects from <paramref name="dailyCosts" /> as of <paramref name="today" />. Days absent
    ///     from the list are zero for the calendar basis and invisible to the activity basis, which is
    ///     the whole difference between the two.
    /// </summary>
    public static CostForecast Project(IReadOnlyList<DashboardDailyCost> dailyCosts, DateTime today)
    {
        var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);

        // The window is applied to the days, not just to the divisor. Capping only the divisor would
        // let a longer history divide 60 days of spend by 30, and would break the guarantee that the
        // calendar projection never exceeds the activity one (which rests on the activity days being a
        // subset of the calendar days).
        var cutoff = DateOnly.FromDateTime(today).AddDays(-(WindowDays - 1));
        var window = dailyCosts.Where(d => d.Date >= cutoff).ToList();
        var spendDays = window.Where(d => d.Cost != 0m).ToList();
        var totalSpend = window.Sum(d => d.Cost);

        if (spendDays.Count == 0)
            return new CostForecast(null, 0, null, 0, totalSpend, daysInMonth);

        // Floored at 1: a series whose first record is a few hours old must divide by one day rather
        // than by a fraction, which would project an absurd month. Measured from the earliest day on
        // record, not the earliest day with spend, so a day that cost nothing still counts as observed.
        var earliest = window.Min(d => d.Date);
        var elapsed = DateOnly.FromDateTime(today).DayNumber - earliest.DayNumber + 1;
        var calendarDays = Math.Max(1, elapsed);
        var activityDays = spendDays.Count;

        return new CostForecast(
            totalSpend / calendarDays * daysInMonth,
            calendarDays,
            totalSpend / activityDays * daysInMonth,
            activityDays,
            totalSpend,
            daysInMonth);
    }
}
