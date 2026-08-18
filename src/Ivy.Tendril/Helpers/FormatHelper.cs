using System.Globalization;

namespace Ivy.Tendril.Helpers;

/// <summary>
///     Shared formatting utilities for human-readable display values.
/// </summary>
/// <remarks>
///     Everything here formats with <see cref="CultureInfo.InvariantCulture" />. These are US dollar
///     amounts and raw token counts, and the app neither sets <c>InvariantGlobalization</c> nor
///     overrides the thread culture — so current-culture formatting renders a cost as "$1,2500"
///     wherever the decimal separator is a comma, a dollar sign against a European decimal mark.
/// </remarks>
public static class FormatHelper
{
    /// <summary>
    ///     Formats a token count as a human-readable string.
    ///     Values >= 1M are formatted as "X.XM", >= 1K as "XK", otherwise as the raw number.
    /// </summary>
    public static string FormatTokens(int tokens)
    {
        return tokens >= 1_000_000 ? (tokens / 1_000_000.0).ToString("F1", CultureInfo.InvariantCulture) + "M"
            : tokens >= 1_000 ? (tokens / 1_000.0).ToString("F0", CultureInfo.InvariantCulture) + "K"
            : tokens.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     Formats a cost value as a dollar amount, e.g. "$12.45" or "$0.0000".
    ///     Pass <paramref name="decimals" /> = 4 for the per-job figures, whose cents digits are
    ///     not enough to distinguish two runs.
    /// </summary>
    public static string FormatCost(decimal cost, int decimals = 2)
    {
        return "$" + cost.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     Formats a whole number with thousands separators, e.g. "1,234,567".
    /// </summary>
    public static string FormatCount(long value)
    {
        return value.ToString("N0", CultureInfo.InvariantCulture);
    }
}
