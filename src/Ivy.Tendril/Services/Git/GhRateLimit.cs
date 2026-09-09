using System.Text.RegularExpressions;

namespace Ivy.Tendril.Services.Git;

internal static class GhRateLimit
{
    internal const string UserMessage =
        "GitHub is rate limiting requests. Tendril retried and is still being throttled, please try again in a few minutes.";

    private static readonly string[] RateLimitIndicators =
    [
        "secondary rate limit",
        "API rate limit exceeded",
        "abuse detection",
        "was submitted too quickly",
        "retry-after"
    ];

    private static readonly Regex RetryAfterRegex = new(@"retry-after:\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static bool IsRateLimitError(string? stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
            return false;

        foreach (var indicator in RateLimitIndicators)
        {
            if (stderr.Contains(indicator, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    internal static TimeSpan? ParseRetryAfter(string? stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
            return null;

        var match = RetryAfterRegex.Match(stderr);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var seconds))
        {
            var clamped = Math.Min(Math.Max(0, seconds), 120);
            return TimeSpan.FromSeconds(clamped);
        }

        return null;
    }

    internal static TimeSpan GetRetryDelay(int attempt, TimeSpan? retryAfter = null)
    {
        if (retryAfter.HasValue)
            return retryAfter.Value;

        double baseSeconds = attempt switch
        {
            <= 1 => 2.0,
            2 => 8.0,
            _ => 30.0
        };

        var jitter = Random.Shared.NextDouble() * 0.20 * baseSeconds;
        return TimeSpan.FromSeconds(baseSeconds + jitter);
    }
}
