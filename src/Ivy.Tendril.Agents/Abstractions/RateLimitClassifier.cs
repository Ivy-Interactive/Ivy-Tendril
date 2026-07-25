using System.Text.RegularExpressions;

namespace Ivy.Tendril.Agents.Abstractions;

/// <summary>
/// How long a provider-side rate limit is expected to last. A per-minute burst limit clears in
/// minutes; an exhausted daily token quota does not, so the two need very different wait times.
/// </summary>
public enum RateLimitScope
{
    /// <summary>The text does not look like a rate limit at all.</summary>
    None,

    /// <summary>A burst/session limit: HTTP 429, "rate limit", "overloaded", "usage limit".</summary>
    ShortTerm,

    /// <summary>A per-day token or request quota has been exhausted.</summary>
    DailyQuota
}

/// <summary>
/// Recognizes provider rate-limit and quota-exhaustion wording in agent output. Shared by the
/// per-provider <see cref="IFailureAnalyzer" /> implementations and by the job scheduler, so the
/// scheduler's cooldown decision uses exactly the same classification the user sees reported.
/// </summary>
public static class RateLimitClassifier
{
    // Checked before the short-term patterns: the Bedrock daily-quota message
    // ("Request rejected (429) - Too many tokens per day") contains both.
    private static readonly Regex[] DailyQuotaPatterns =
    [
        new(@"tokens?\s+per\s+day", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"per[-\s]?day\s+(token\s+)?(limit|quota)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"daily\s+(token\s+|request\s+)?(limit|quota)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"quota\s+(has\s+been\s+)?exceed(ed)?", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    private static readonly Regex[] ShortTermPatterns =
    [
        new(@"\b429\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"rate[_\s-]?limit", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"too\s+many\s+requests", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"overloaded_error", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"(session|usage)\s+limit", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"limit\s+reached", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    /// <summary>
    /// Classifies a single block of text. Returns <see cref="RateLimitScope.None" /> for null,
    /// empty, or unrelated text.
    /// </summary>
    public static RateLimitScope Classify(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return RateLimitScope.None;

        if (DailyQuotaPatterns.Any(p => p.IsMatch(text)))
            return RateLimitScope.DailyQuota;

        if (ShortTermPatterns.Any(p => p.IsMatch(text)))
            return RateLimitScope.ShortTerm;

        return RateLimitScope.None;
    }

    /// <summary>
    /// Classifies several lines at once. The strongest scope found wins, so a daily-quota line
    /// anywhere in the output is not masked by an unrelated 429 on another line.
    /// </summary>
    public static RateLimitScope Classify(IEnumerable<string> lines)
    {
        var strongest = RateLimitScope.None;
        foreach (var line in lines)
        {
            var scope = Classify(line);
            if (scope == RateLimitScope.DailyQuota) return RateLimitScope.DailyQuota;
            if (scope > strongest) strongest = scope;
        }
        return strongest;
    }
}
