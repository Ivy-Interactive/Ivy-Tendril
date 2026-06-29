using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Copilot;

namespace Ivy.Tendril.Agents.Test.Copilot;

public class CopilotFailureAnalyzerTests
{
    private readonly CopilotFailureAnalyzer _analyzer = new();

    [Fact]
    public void Analyze_TimedOut_ReturnsTimeout()
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.Copilot,
            TimedOut = true,
            IdleTimeout = false,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.Timeout, result.Kind);
        Assert.True(result.IsRetryable);
        Assert.Contains("Copilot", result.Reason);
        Assert.Contains("timeout", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_IdleTimeout_ReturnsIdleTimeout()
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.Copilot,
            TimedOut = true,
            IdleTimeout = true,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.IdleTimeout, result.Kind);
        Assert.True(result.IsRetryable);
        Assert.Contains("Copilot", result.Reason);
        Assert.Contains("idle", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("rate limit exceeded")]
    [InlineData("Error 429: too many requests")]
    [InlineData("Too Many Requests")]
    public void Analyze_RateLimit_ReturnsRateLimit(string stderrLine)
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.Copilot,
            StderrLines = [stderrLine],
            ExitCode = 1,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.RateLimit, result.Kind);
        Assert.True(result.IsRetryable);
    }

    [Theory]
    [InlineData("auth failed")]
    [InlineData("please login")]
    [InlineData("sign in required")]
    [InlineData("unauthorized access")]
    [InlineData("HTTP 401")]
    [InlineData("HTTP 403 Forbidden")]
    public void Analyze_AuthError_ReturnsAuthError(string stderrLine)
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.Copilot,
            StderrLines = [stderrLine],
            ExitCode = 1,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.AuthError, result.Kind);
        Assert.False(result.IsRetryable);
    }

    [Fact]
    public void Analyze_AuthError_SuggestsCopilotLogin()
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.Copilot,
            StderrLines = ["unauthorized"],
            ExitCode = 1,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.AuthError, result.Kind);
        Assert.Contains("copilot login", result.Suggestion!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("invalid model specified")]
    [InlineData("model not found")]
    [InlineData("model does not exist")]
    public void Analyze_InvalidModel_ReturnsInvalidModel(string stderrLine)
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.Copilot,
            StderrLines = [stderrLine],
            ExitCode = 1,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.InvalidModel, result.Kind);
        Assert.False(result.IsRetryable);
    }

    [Theory]
    [InlineData("network error")]
    [InlineData("connection refused")]
    [InlineData("ECONNREFUSED")]
    [InlineData("ETIMEDOUT")]
    [InlineData("dns resolution failed")]
    public void Analyze_NetworkError_ReturnsNetworkError(string stderrLine)
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.Copilot,
            StderrLines = [stderrLine],
            ExitCode = 1,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.NetworkError, result.Kind);
        Assert.True(result.IsRetryable);
    }

    [Fact]
    public void Analyze_NonZeroExitCode_ReturnsProcessCrash()
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.Copilot,
            ExitCode = 137,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.ProcessCrash, result.Kind);
        Assert.True(result.IsRetryable);
        Assert.Contains("137", result.Reason);
        Assert.Contains("Copilot", result.Reason);
    }

    [Fact]
    public void Analyze_ZeroExitNoIssues_ReturnsUnknown()
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.Copilot,
            ExitCode = 0,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.Unknown, result.Kind);
        Assert.False(result.IsRetryable);
    }

    [Fact]
    public void Analyze_Timeout_TakesPriority_OverStderr()
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.Copilot,
            TimedOut = true,
            StderrLines = ["rate limit exceeded"],
            ExitCode = 1,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.Timeout, result.Kind);
    }
}
