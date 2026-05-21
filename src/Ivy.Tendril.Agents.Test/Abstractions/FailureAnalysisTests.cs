using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Test.Abstractions;

public class FailureAnalysisTests
{
    [Fact]
    public void FailureContext_CreatesCorrectly()
    {
        var ctx = new FailureContext
        {
            Events = [new TextEvent { Kind = AgentEventKind.Text, Text = "x" }],
            StderrLines = ["error: something failed"],
            ExitCode = 1,
            TimedOut = false,
            IdleTimeout = false,
            AgentId = AgentId.Claude,
        };

        Assert.Single(ctx.Events);
        Assert.Single(ctx.StderrLines);
        Assert.Equal(1, ctx.ExitCode);
        Assert.Equal(AgentId.Claude, ctx.AgentId);
    }

    [Fact]
    public void FailureContext_Defaults()
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.Gemini,
        };

        Assert.Empty(ctx.StderrLines);
        Assert.Null(ctx.ExitCode);
        Assert.False(ctx.TimedOut);
        Assert.False(ctx.IdleTimeout);
    }

    [Fact]
    public void FailureAnalysis_CreatesCorrectly()
    {
        var analysis = new FailureAnalysis
        {
            Kind = FailureKind.RateLimit,
            Reason = "Too many requests",
            ContextLines = ["429 Too Many Requests"],
            IsRetryable = true,
            Suggestion = "Wait 60 seconds before retrying",
        };

        Assert.Equal(FailureKind.RateLimit, analysis.Kind);
        Assert.True(analysis.IsRetryable);
        Assert.Equal("Wait 60 seconds before retrying", analysis.Suggestion);
    }

    [Theory]
    [InlineData(FailureKind.RateLimit)]
    [InlineData(FailureKind.AuthError)]
    [InlineData(FailureKind.InvalidModel)]
    [InlineData(FailureKind.Timeout)]
    [InlineData(FailureKind.IdleTimeout)]
    [InlineData(FailureKind.ProcessCrash)]
    [InlineData(FailureKind.ValidationError)]
    [InlineData(FailureKind.PermissionBlocked)]
    [InlineData(FailureKind.NetworkError)]
    [InlineData(FailureKind.Unknown)]
    public void FailureKind_AllValues_Exist(FailureKind kind)
    {
        var analysis = new FailureAnalysis
        {
            Kind = kind,
            Reason = "test",
        };
        Assert.Equal(kind, analysis.Kind);
    }
}
