using System.Text.RegularExpressions;

namespace Ivy.Tendril.Helpers;

/// <summary>
///     Identity of a GitHub PR URL, for the cases that have to compare two URLs that name the same PR.
///     A URL reaches plan.yaml in whatever form the agent had it in, so the same PR can be recorded as
///     the base URL, with a trailing slash, with a /files suffix or with a #discussion fragment.
/// </summary>
public static class PrUrlHelper
{
    private static readonly Regex PrUrlPattern = new(
        @"^https?://github\.com/(?<owner>[^/\s]+)/(?<repo>[^/\s]+)/pull/(?<number>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    ///     "owner/repo#number", lower cased, or null when the string is not a GitHub PR URL.
    /// </summary>
    public static string? CanonicalKey(string? url)
    {
        if (url == null) return null;
        var m = PrUrlPattern.Match(url.Trim());
        return m.Success
            ? $"{m.Groups["owner"].Value}/{m.Groups["repo"].Value}#{m.Groups["number"].Value}".ToLowerInvariant()
            : null;
    }

    /// <summary>
    ///     True when both strings are PR URLs naming the same PR, or when they are byte equal ignoring
    ///     case. The literal comparison is the fallback for a value that never parsed, so a malformed
    ///     entry can still be addressed by exactly what is stored.
    /// </summary>
    public static bool SamePr(string? a, string? b)
    {
        if (string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase)) return true;

        var keyA = CanonicalKey(a);
        return keyA != null && keyA == CanonicalKey(b);
    }
}
