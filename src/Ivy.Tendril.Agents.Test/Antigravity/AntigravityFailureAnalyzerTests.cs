using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Antigravity;

namespace Ivy.Tendril.Agents.Test.Antigravity;

public class AntigravityFailureAnalyzerTests
{
    private readonly AntigravityFailureAnalyzer _analyzer = new();

    [Fact]
    public void Analyze_TimedOut_ReturnsTimeout()
    {
        var context = new FailureContext { TimedOut = true, IdleTimeout = false, AgentId = AgentId.Antigravity, Events = [] };
        var result = _analyzer.Analyze(context);

        Assert.Equal(FailureKind.Timeout, result.Kind);
        Assert.True(result.IsRetryable);
    }

    [Fact]
    public void Analyze_IdleTimeout_ReturnsIdleTimeout()
    {
        var context = new FailureContext { TimedOut = true, IdleTimeout = true, AgentId = AgentId.Antigravity, Events = [] };
        var result = _analyzer.Analyze(context);

        Assert.Equal(FailureKind.IdleTimeout, result.Kind);
        Assert.True(result.IsRetryable);
    }

    [Theory]
    [InlineData("Error: quota exceeded")]
    [InlineData("rate limit reached")]
    [InlineData("HTTP 429 Too Many Requests")]
    [InlineData("too many requests, please slow down")]
    [InlineData("RESOURCE_EXHAUSTED: Quota exceeded")]
    public void Analyze_RateLimitKeywords_ReturnsRateLimit(string stderr)
    {
        var context = new FailureContext { StderrLines = [stderr], AgentId = AgentId.Antigravity, Events = [] };
        var result = _analyzer.Analyze(context);

        Assert.Equal(FailureKind.RateLimit, result.Kind);
        Assert.True(result.IsRetryable);
    }

    [Theory]
    [InlineData("Error: not logged in")]
    [InlineData("You are not logged into Antigravity")]
    [InlineData("oauth token expired")]
    [InlineData("unauthorized access")]
    [InlineData("HTTP 401")]
    [InlineData("HTTP 403 Forbidden")]
    public void Analyze_AuthKeywords_ReturnsAuthError(string stderr)
    {
        var context = new FailureContext { StderrLines = [stderr], AgentId = AgentId.Antigravity, Events = [] };
        var result = _analyzer.Analyze(context);

        Assert.Equal(FailureKind.AuthError, result.Kind);
        Assert.False(result.IsRetryable);
    }

    [Theory]
    [InlineData("network error occurred")]
    [InlineData("connection refused")]
    [InlineData("ECONNREFUSED")]
    [InlineData("ETIMEDOUT")]
    [InlineData("dns resolution failed")]
    public void Analyze_NetworkKeywords_ReturnsNetworkError(string stderr)
    {
        var context = new FailureContext { StderrLines = [stderr], AgentId = AgentId.Antigravity, Events = [] };
        var result = _analyzer.Analyze(context);

        Assert.Equal(FailureKind.NetworkError, result.Kind);
        Assert.True(result.IsRetryable);
    }

    [Fact]
    public void Analyze_NonZeroExitCode_ReturnsProcessCrash()
    {
        var context = new FailureContext { ExitCode = 1, StderrLines = ["something went wrong"], AgentId = AgentId.Antigravity, Events = [] };
        var result = _analyzer.Analyze(context);

        Assert.Equal(FailureKind.ProcessCrash, result.Kind);
        Assert.True(result.IsRetryable);
    }

    [Fact]
    public void Analyze_ExitCode2_IsNotRetryable()
    {
        var context = new FailureContext { ExitCode = 2, StderrLines = ["usage error"], AgentId = AgentId.Antigravity, Events = [] };
        var result = _analyzer.Analyze(context);

        Assert.Equal(FailureKind.ProcessCrash, result.Kind);
        Assert.False(result.IsRetryable);
    }

    [Fact]
    public void Analyze_NoMatchingPatterns_ReturnsUnknown()
    {
        var context = new FailureContext { StderrLines = ["some unrecognized error"], AgentId = AgentId.Antigravity, Events = [] };
        var result = _analyzer.Analyze(context);

        Assert.Equal(FailureKind.Unknown, result.Kind);
        Assert.False(result.IsRetryable);
    }
}
