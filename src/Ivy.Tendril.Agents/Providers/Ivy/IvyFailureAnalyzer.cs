using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.OpenCode;

namespace Ivy.Tendril.Agents.Providers.Ivy;

public sealed class IvyFailureAnalyzer : IFailureAnalyzer
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
            .Replace("OpenCode", "Ivy Agent")
            .Replace("opencode", "ivy");

        var suggestion = result.Suggestion?
            .Replace("OpenCode", "Ivy Agent")
            .Replace("opencode", "ivy")
            .Replace("Run 'ivy providers login' to authenticate", "Ensure you are logged in to your @ivy.app account in Settings -> Account, or enter an API key in settings.");

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
