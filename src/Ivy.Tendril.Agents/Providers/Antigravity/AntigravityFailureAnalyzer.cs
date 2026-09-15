using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Runtime;

namespace Ivy.Tendril.Agents.Providers.Antigravity;

public sealed class AntigravityFailureAnalyzer : IFailureAnalyzer
{
    public FailureAnalysis Analyze(FailureContext context)
    {
        if (context.TimedOut)
        {
            return new FailureAnalysis
            {
                Kind = context.IdleTimeout ? FailureKind.IdleTimeout : FailureKind.Timeout,
                Reason = context.IdleTimeout
                    ? "Antigravity went idle beyond the configured threshold"
                    : "Antigravity exceeded the total timeout",
                IsRetryable = true,
                Suggestion = "Increase timeout or simplify the prompt",
            };
        }

        // A trailing tool failure with no successful response after it is a structured, authoritative
        // signal straight from the agent's event stream — it should win over the stderr heuristics below.
        if (context.Events.Count > 0 && context.Events[^1] is ToolResultEvent { IsError: true } trailingError)
        {
            return new FailureAnalysis
            {
                Kind = FailureKind.ValidationError,
                Reason = $"Tool '{trailingError.ToolName}' failed: {trailingError.Output}",
                ContextLines = context.StderrLines,
                IsRetryable = false,
            };
        }

        var stderr = string.Join("\n", context.StderrLines);

        // Quota and auth terms live in ProviderErrorClassifier, so this analyzer, the event parser,
        // the doctor model probe and the job-side fail-fast paths all recognize the same wall.
        switch (ProviderErrorClassifier.Classify(stderr))
        {
            case ProviderErrorClassifier.ProviderErrorKind.Quota:
                return new FailureAnalysis
                {
                    Kind = FailureKind.RateLimit,
                    Reason = "Rate limited or quota exceeded",
                    ContextLines = context.StderrLines,
                    IsRetryable = true,
                    Suggestion = "Wait before retrying or switch to a different model",
                };

            case ProviderErrorClassifier.ProviderErrorKind.Auth:
                return new FailureAnalysis
                {
                    Kind = FailureKind.AuthError,
                    Reason = "Authentication failure",
                    ContextLines = context.StderrLines,
                    IsRetryable = false,
                    Suggestion = "Run 'agy' to re-authenticate",
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

        var lastStderr = context.StderrLines.LastOrDefault(l => !string.IsNullOrWhiteSpace(l));

        if (context.ExitCode is not null and not 0)
        {
            return new FailureAnalysis
            {
                Kind = FailureKind.ProcessCrash,
                Reason = lastStderr != null
                    ? $"Antigravity exited with code {context.ExitCode}: {lastStderr}"
                    : $"Antigravity exited with code {context.ExitCode}",
                ContextLines = context.StderrLines,
                IsRetryable = context.ExitCode != 2,
            };
        }

        return new FailureAnalysis
        {
            Kind = FailureKind.Unknown,
            Reason = lastStderr != null
                ? $"Antigravity failed: {lastStderr}"
                : $"Antigravity failed with an unknown error (exit code {context.ExitCode?.ToString() ?? "unknown"})",
            ContextLines = context.StderrLines,
            IsRetryable = false,
        };
    }

    private static bool ContainsAny(string text, params string[] terms)
        => terms.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));
}
