using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Providers.OpenCode;

public sealed class OpenCodeFailureAnalyzer : IFailureAnalyzer
{
    public FailureAnalysis Analyze(FailureContext context)
    {
        if (context.TimedOut)
        {
            return new FailureAnalysis
            {
                Kind = context.IdleTimeout ? FailureKind.IdleTimeout : FailureKind.Timeout,
                Reason = context.IdleTimeout
                    ? "OpenCode went idle beyond the configured threshold"
                    : "OpenCode exceeded the total timeout",
                IsRetryable = true,
                Suggestion = "Increase timeout or simplify the prompt",
            };
        }

        // OpenCode exit code is always 0, so check events for error signals first
        var errorEvent = context.Events.OfType<ErrorEvent>().LastOrDefault();
        if (errorEvent is not null)
        {
            if (errorEvent.IsAuthError)
            {
                return new FailureAnalysis
                {
                    Kind = FailureKind.AuthError,
                    Reason = "Authentication failure",
                    ContextLines = [errorEvent.Message],
                    IsRetryable = false,
                    Suggestion = "Run 'opencode providers login' to authenticate",
                };
            }

            if (errorEvent.IsRetryable)
            {
                // Determine if rate limit or network based on message content
                var msg = errorEvent.Message;
                if (ContainsAny(msg, "rate limit", "429", "too many requests"))
                {
                    return new FailureAnalysis
                    {
                        Kind = FailureKind.RateLimit,
                        Reason = "Rate limited by the API",
                        ContextLines = [errorEvent.Message],
                        IsRetryable = true,
                        Suggestion = "Wait before retrying or switch to a different model",
                    };
                }

                return new FailureAnalysis
                {
                    Kind = FailureKind.NetworkError,
                    Reason = "A retryable error occurred",
                    ContextLines = [errorEvent.Message],
                    IsRetryable = true,
                    Suggestion = "Check network connection and retry",
                };
            }

            return new FailureAnalysis
            {
                Kind = FailureKind.Unknown,
                Reason = errorEvent.Message,
                ContextLines = [errorEvent.Message],
                IsRetryable = false,
                Suggestion = "Check the error details and retry",
            };
        }

        var stderr = string.Join("\n", context.StderrLines);

        if (ContainsAny(stderr, "rate limit", "429", "too many requests"))
        {
            return new FailureAnalysis
            {
                Kind = FailureKind.RateLimit,
                Reason = "Rate limited by the API",
                ContextLines = context.StderrLines,
                IsRetryable = true,
                Suggestion = "Wait before retrying or switch to a different model",
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
                Suggestion = "Run 'opencode providers login' to authenticate",
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
                Suggestion = "Check model name (format: provider/model-name) or use a different model",
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
                Reason = $"OpenCode exited with code {context.ExitCode}",
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
