using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Providers.Claude;

public sealed class ClaudeFailureAnalyzer : IFailureAnalyzer
{
    public FailureAnalysis Analyze(FailureContext context)
    {
        if (context.TimedOut)
        {
            return new FailureAnalysis
            {
                Kind = context.IdleTimeout ? FailureKind.IdleTimeout : FailureKind.Timeout,
                Reason = context.IdleTimeout
                    ? "Claude Code went idle beyond the configured threshold"
                    : "Claude Code exceeded the total timeout",
                IsRetryable = true,
                Suggestion = "Increase timeout or simplify the prompt",
            };
        }

        var stderr = string.Join("\n", context.StderrLines);
        var lastResultResponse = context.Events.OfType<ResultEvent>().LastOrDefault()?.Response ?? "";

        // Shared with the job scheduler (which parks and auto-retries rate-limited jobs), so the
        // cooldown it applies always matches the reason reported here.
        var rateLimitScope = RateLimitClassifier.Classify([stderr, lastResultResponse]);
        if (rateLimitScope != RateLimitScope.None)
        {
            var isDailyQuota = rateLimitScope == RateLimitScope.DailyQuota;
            return new FailureAnalysis
            {
                Kind = FailureKind.RateLimit,
                Reason = isDailyQuota ? "Daily token quota exhausted" : "Rate limited by the API",
                ContextLines = context.StderrLines,
                IsRetryable = true,
                Suggestion = isDailyQuota
                    ? "Waiting for the quota window to reset before retrying"
                    : "Wait before retrying or switch to a different model",
            };
        }

        if (ContainsAny(stderr, "auth", "login", "sign in", "unauthorized", "401", "403"))
        {
            return new FailureAnalysis
            {
                Kind = FailureKind.AuthError,
                Reason = "Authentication failure",
                ContextLines = context.StderrLines,
                IsRetryable = false,
                Suggestion = "Run 'claude login' to re-authenticate",
            };
        }

        if (ContainsAny(stderr, "model", "invalid model", "not found", "does not exist"))
        {
            return new FailureAnalysis
            {
                Kind = FailureKind.InvalidModel,
                Reason = "The specified model is not available",
                ContextLines = context.StderrLines,
                IsRetryable = false,
                Suggestion = "Check model name or use a different model",
            };
        }

        if (ContainsAny(stderr, "network", "connection", "ECONNREFUSED", "ETIMEDOUT", "dns"))
        {
            return new FailureAnalysis
            {
                Kind = FailureKind.NetworkError,
                Reason = "Network connectivity issue",
                ContextLines = context.StderrLines,
                IsRetryable = true,
                Suggestion = "Check network connection and retry",
            };
        }

        var lastDenials = context.Events
            .OfType<ResultEvent>()
            .LastOrDefault()?.PermissionDenials;

        if (lastDenials is { Count: > 0 })
        {
            return new FailureAnalysis
            {
                Kind = FailureKind.PermissionBlocked,
                Reason = $"Permission denied for {lastDenials.Count} tool call(s)",
                ContextLines = lastDenials.Select(d => $"{d.ToolName}: {d.InputSummary}").ToList(),
                IsRetryable = false,
                Suggestion = "Grant the required permissions or adjust the prompt to avoid restricted operations",
            };
        }

        var lastStderr = context.StderrLines.LastOrDefault(l => !string.IsNullOrWhiteSpace(l));

        if (context.ExitCode is not null and not 0)
        {
            return new FailureAnalysis
            {
                Kind = FailureKind.ProcessCrash,
                Reason = lastStderr != null
                    ? $"Claude Code exited with code {context.ExitCode}: {lastStderr}"
                    : $"Claude Code exited with code {context.ExitCode}",
                ContextLines = context.StderrLines,
                IsRetryable = true,
            };
        }

        return new FailureAnalysis
        {
            Kind = FailureKind.Unknown,
            Reason = lastStderr != null
                ? $"Claude Code failed: {lastStderr}"
                : $"Claude Code failed with an unknown error (exit code {context.ExitCode?.ToString() ?? "unknown"})",
            ContextLines = context.StderrLines,
            IsRetryable = false,
        };
    }

    private static bool ContainsAny(string text, params string[] terms)
        => terms.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));
}
