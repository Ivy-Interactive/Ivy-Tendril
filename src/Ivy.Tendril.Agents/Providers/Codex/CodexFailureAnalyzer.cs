using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Providers.Codex;

public sealed class CodexFailureAnalyzer : IFailureAnalyzer
{
    public FailureAnalysis Analyze(FailureContext context)
    {
        if (context.TimedOut)
        {
            return new FailureAnalysis
            {
                Kind = context.IdleTimeout ? FailureKind.IdleTimeout : FailureKind.Timeout,
                Reason = context.IdleTimeout
                    ? "Codex went idle beyond the configured threshold"
                    : "Codex exceeded the total timeout",
                IsRetryable = true,
                Suggestion = "Increase timeout or simplify the prompt",
            };
        }

        var errorEvent = context.Events.OfType<ErrorEvent>().LastOrDefault();
        var errorText = errorEvent?.Message ?? "";
        var stderr = string.Join("\n", context.StderrLines);
        var combined = string.IsNullOrWhiteSpace(errorText) ? stderr : $"{errorText}\n{stderr}";

        if (ContainsAny(combined, "rate limit", "429", "too many requests", "usage limit", "hit your usage limit"))
        {
            return new FailureAnalysis
            {
                Kind = FailureKind.RateLimit,
                Reason = !string.IsNullOrWhiteSpace(errorText) ? errorText : "Rate limited or usage limit reached by Codex API",
                ContextLines = context.StderrLines,
                IsRetryable = true,
                Suggestion = "Wait before retrying, upgrade your ChatGPT plan, or switch to a different model or agent",
            };
        }

        if (ContainsAny(combined, "not supported when using Codex with a ChatGPT account", "not supported"))
        {
            return new FailureAnalysis
            {
                Kind = FailureKind.InvalidModel,
                Reason = !string.IsNullOrWhiteSpace(errorText) ? errorText : "The specified model is not supported with this account",
                ContextLines = context.StderrLines,
                IsRetryable = false,
                Suggestion = "Select a supported model (e.g. gpt-5.6-terra) or authenticate with an API key",
            };
        }

        if (ContainsAny(combined, "auth", "login", "sign in", "unauthorized", "401", "403"))
        {
            return new FailureAnalysis
            {
                Kind = FailureKind.AuthError,
                Reason = !string.IsNullOrWhiteSpace(errorText) ? errorText : "Authentication failure",
                ContextLines = context.StderrLines,
                IsRetryable = false,
                Suggestion = "Run 'codex login' to authenticate",
            };
        }

        if (ContainsAny(combined, "model", "invalid model", "not found", "does not exist"))
        {
            return new FailureAnalysis
            {
                Kind = FailureKind.InvalidModel,
                Reason = !string.IsNullOrWhiteSpace(errorText) ? errorText : "The specified model is not available",
                ContextLines = context.StderrLines,
                IsRetryable = false,
                Suggestion = "Check model name or use a different model (e.g., gpt-5.6-terra, o4-mini)",
            };
        }

        if (ContainsAny(combined, "network", "connection", "ECONNREFUSED", "ETIMEDOUT", "dns"))
        {
            return new FailureAnalysis
            {
                Kind = FailureKind.NetworkError,
                Reason = !string.IsNullOrWhiteSpace(errorText) ? errorText : "Network connectivity issue",
                ContextLines = context.StderrLines,
                IsRetryable = true,
                Suggestion = "Check network connection and retry",
            };
        }

        var lastMessage = !string.IsNullOrWhiteSpace(errorText)
            ? errorText
            : context.StderrLines.LastOrDefault(l => !string.IsNullOrWhiteSpace(l));

        if (context.ExitCode is not null and not 0)
        {
            return new FailureAnalysis
            {
                Kind = FailureKind.ProcessCrash,
                Reason = lastMessage != null
                    ? $"Codex exited with code {context.ExitCode}: {lastMessage}"
                    : $"Codex exited with code {context.ExitCode}",
                ContextLines = context.StderrLines,
                IsRetryable = true,
            };
        }

        return new FailureAnalysis
        {
            Kind = FailureKind.Unknown,
            Reason = lastMessage != null
                ? $"Codex failed: {lastMessage}"
                : $"Codex failed with an unknown error (exit code {context.ExitCode?.ToString() ?? "unknown"})",
            ContextLines = context.StderrLines,
            IsRetryable = false,
        };
    }

    private static bool ContainsAny(string text, params string[] terms)
        => terms.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));
}
