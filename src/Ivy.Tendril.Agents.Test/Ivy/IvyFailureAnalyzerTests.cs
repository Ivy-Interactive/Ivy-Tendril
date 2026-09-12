using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Ivy;
using Ivy.Tendril.Agents.Providers.OpenCode;

namespace Ivy.Tendril.Agents.Test.Providers.Ivy;

public class IvyFailureAnalyzerTests
{
    private readonly IvyFailureAnalyzer _analyzer = new();

    [Fact]
    public void Analyze_TruncationErrorEvent_KindAndMessageSurviveRewrite()
    {
        var errorEvent = new ErrorEvent
        {
            Kind = AgentEventKind.Error,
            Message = "Model output truncated at the max output token limit (4096 output tokens in the final step); any pending tool call was aborted.",
            Code = OpenCodeEventParser.OutputTruncatedCode,
            IsRetryable = true,
        };

        var ctx = new FailureContext
        {
            Events = [errorEvent],
            AgentId = AgentId.Ivy,
            ExitCode = 0,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.OutputTruncated, result.Kind);
        Assert.True(result.IsRetryable);
        Assert.DoesNotContain("OpenCode", result.Reason);
        Assert.Contains("output token limit", result.Reason, StringComparison.OrdinalIgnoreCase);
    }
}
