using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.OpenCode;

namespace Ivy.Tendril.Agents.Providers.OpenAiProxy;

public sealed class OpenAiProxyFailureAnalyzer : IFailureAnalyzer
{
    private readonly OpenCodeFailureAnalyzer _inner = new();

    public FailureAnalysis Analyze(FailureContext context)
    {
        var originalContext = new FailureContext
        {
            Events = context.Events,
            StderrLines = context.StderrLines,
            ExitCode = context.ExitCode,
            TimedOut = context.TimedOut,
            IdleTimeout = context.IdleTimeout,
            AgentId = AgentId.OpenCode,
        };

        var result = _inner.Analyze(originalContext);

        var reason = result.Reason
            .Replace("OpenCode", "OpenAI Proxy")
            .Replace("opencode", "openaiproxy");

        var suggestion = result.Suggestion?
            .Replace("OpenCode", "OpenAI Proxy")
            .Replace("opencode", "openaiproxy")
            .Replace("Run 'ivy providers login' to authenticate", "Ensure you have entered a valid Base URL and API Key in Settings -> Coding Agent.");

        return new FailureAnalysis
        {
            Kind = result.Kind,
            Reason = reason,
            ContextLines = result.ContextLines,
            IsRetryable = result.IsRetryable,
            Suggestion = suggestion
        };
    }
}
