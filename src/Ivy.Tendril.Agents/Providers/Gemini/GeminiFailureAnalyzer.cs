using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Providers.Gemini;

public sealed class GeminiFailureAnalyzer : IFailureAnalyzer
{
    public FailureAnalysis Analyze(FailureContext context)
    {
        if (context.TimedOut)
        {
            return new FailureAnalysis
            {
                Kind = context.IdleTimeout ? FailureKind.IdleTimeout : FailureKind.Timeout,
                Reason = context.IdleTimeout
                    ? "Gemini CLI went idle beyond the configured threshold"
                    : "Gemini CLI exceeded the total timeout",
                IsRetryable = true,
                Suggestion = "Increase timeout or simplify the prompt",
            };
        }

        var stderr = string.Join("\n", context.StderrLines);

        if (ContainsAny(stderr, "quota", "rate limit", "429", "too many requests", "RESOURCE_EXHAUSTED"))
        {
            return new FailureAnalysis
            {
                Kind = FailureKind.RateLimit,
                Reason = "Rate limited or quota exceeded",
                ContextLines = context.StderrLines,
                IsRetryable = true,
                Suggestion = "Wait before retrying or switch to a different model",
            };
        }

        if (ContainsAny(stderr, "auth", "login", "sign in", "unauthorized", "401", "403", "oauth"))
        {
            return new FailureAnalysis
            {
                Kind = FailureKind.AuthError,
                Reason = "Authentication failure",
                ContextLines = context.StderrLines,
                IsRetryable = false,
                Suggestion = "Run 'gemini auth' to authenticate",
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

        if (context.ExitCode is not null and not 0)
        {
            return new FailureAnalysis
            {
                Kind = FailureKind.ProcessCrash,
                Reason = $"Gemini CLI exited with code {context.ExitCode}",
                ContextLines = context.StderrLines,
                IsRetryable = true,
            };
        }

        return new FailureAnalysis
        {
            Kind = FailureKind.Unknown,
            Reason = "Unknown failure",
            ContextLines = context.StderrLines,
            IsRetryable = false,
        };
    }

    private static bool ContainsAny(string text, params string[] terms)
        => terms.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));
}
