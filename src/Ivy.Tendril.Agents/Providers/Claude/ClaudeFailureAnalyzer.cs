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
        var textResponses = string.Join("\n", context.Events.OfType<TextEvent>().Select(e => e.Text));
        var errorMessages = string.Join("\n", context.Events.OfType<ErrorEvent>().Select(e => e.Message));
        var allOutput = $"{stderr}\n{lastResultResponse}\n{textResponses}\n{errorMessages}";

        if (ContainsAny(allOutput, "data retention mode"))
        {
            var matchingLines = context.StderrLines
                .Concat(context.Events.OfType<TextEvent>().Select(e => e.Text))
                .Concat(context.Events.OfType<ErrorEvent>().Select(e => e.Message))
                .Concat(string.IsNullOrEmpty(lastResultResponse) ? [] : [lastResultResponse])
                .SelectMany(t => t.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                .Where(l => l.Contains("data retention mode", StringComparison.OrdinalIgnoreCase))
                .ToList();

            return new FailureAnalysis
            {
                Kind = FailureKind.InvalidModel,
                Reason = "The selected model requires an AWS Bedrock data retention mode (such as 'aws_review') that is not enabled in your AWS Bedrock account or project.",
                ContextLines = matchingLines.Count > 0 ? matchingLines : context.StderrLines,
                IsRetryable = false,
                Suggestion = "Switch to a supported model (such as Claude Opus or Claude Sonnet), or configure your AWS Bedrock data retention mode to 'aws_review' in the AWS Bedrock console or via the AWS data retention API.",
            };
        }

        if (ContainsAny(stderr, "rate limit", "429", "too many requests", "session limit", "usage limit")
            || ContainsAny(lastResultResponse, "rate limit", "session limit", "usage limit"))
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
