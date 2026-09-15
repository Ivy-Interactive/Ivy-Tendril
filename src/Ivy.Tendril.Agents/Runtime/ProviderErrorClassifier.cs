using System.Text.RegularExpressions;

namespace Ivy.Tendril.Agents.Runtime;

/// <summary>
///     Recognizes the two provider-level walls that stop an agent before it does any work: an
///     exhausted quota and a failed authentication. Shared by the event parsers (which turn a
///     provider error into a visible <c>ErrorEvent</c>), the failure analyzers, the doctor model
///     probe and the job-side fail-fast paths, so all of them agree on what a quota wall looks like
///     instead of each carrying its own term list.
/// </summary>
public static class ProviderErrorClassifier
{
    public enum ProviderErrorKind
    {
        None,
        Quota,
        Auth
    }

    /// <summary>
    ///     Quota / rate-limit terms. These are the terms <see cref="Providers.Antigravity.AntigravityFailureAnalyzer" />
    ///     carried before it was refactored onto this classifier.
    /// </summary>
    private static readonly string[] QuotaTerms =
        ["quota", "rate limit", "429", "too many requests", "RESOURCE_EXHAUSTED"];

    /// <summary>
    ///     Authentication terms. Same provenance as <see cref="QuotaTerms" />, plus the generic
    ///     "authentication" wording that the doctor probe sees.
    /// </summary>
    private static readonly string[] AuthTerms =
    [
        "not logged in", "you are not logged into", "oauth", "unauthorized", "401", "403",
        "authentication", "unauthenticated"
    ];

    /// <summary>
    ///     gRPC/Google-style status names a provider puts in front of its message. Matched before the
    ///     numeric form so <c>RESOURCE_EXHAUSTED (code 429)</c> reports the more specific name.
    /// </summary>
    private static readonly Regex SymbolicCode = new(
        @"\b(RESOURCE_EXHAUSTED|PERMISSION_DENIED|UNAUTHENTICATED|DEADLINE_EXCEEDED|UNAVAILABLE|INVALID_ARGUMENT|NOT_FOUND|FAILED_PRECONDITION)\b",
        RegexOptions.Compiled);

    /// <summary>
    ///     Only the HTTP statuses a provider failure actually reports, rather than any three-digit
    ///     number: an unconstrained pattern happily reads "attempt 429" out of prose that is not a code.
    /// </summary>
    private static readonly Regex NumericCode = new(
        @"\b(400|401|402|403|404|408|409|422|429|500|502|503|504)\b",
        RegexOptions.Compiled);

    public static ProviderErrorKind Classify(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return ProviderErrorKind.None;

        // Quota first, matching the order the analyzers already used: a 429 that also mentions oauth
        // is a quota wall, and retrying is the right advice for it.
        if (ContainsAny(text, QuotaTerms))
            return ProviderErrorKind.Quota;

        if (ContainsAny(text, AuthTerms))
            return ProviderErrorKind.Auth;

        return ProviderErrorKind.None;
    }

    /// <summary>
    ///     Pulls the provider's own error code out of a message, e.g. <c>RESOURCE_EXHAUSTED</c> or
    ///     <c>429</c>. Null when the message carries no recognizable code.
    /// </summary>
    public static string? ExtractCode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var symbolic = SymbolicCode.Match(text);
        if (symbolic.Success)
            return symbolic.Value;

        var numeric = NumericCode.Match(text);
        return numeric.Success ? numeric.Value : null;
    }

    /// <summary>
    ///     A quota wall clears by itself, so it is worth retrying; an auth failure will not.
    /// </summary>
    public static bool IsRetryable(ProviderErrorKind kind) => kind == ProviderErrorKind.Quota;

    /// <summary>
    ///     Human-readable name of the condition, for the message shown on a failed job.
    /// </summary>
    public static string Describe(ProviderErrorKind kind) => kind switch
    {
        ProviderErrorKind.Quota => "provider quota exhausted",
        ProviderErrorKind.Auth => "provider authentication failed",
        _ => "provider error",
    };

    private static bool ContainsAny(string text, string[] terms)
        => terms.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));
}
