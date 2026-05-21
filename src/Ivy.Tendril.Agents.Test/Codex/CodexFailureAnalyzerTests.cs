using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Codex;

namespace Ivy.Tendril.Agents.Test.Codex;

public class CodexFailureAnalyzerTests
{
    private readonly CodexFailureAnalyzer _analyzer = new();

    [Fact]
    public void Analyze_Timeout_ReturnsTimeout()
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.Codex,
            TimedOut = true,
            IdleTimeout = false,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.Timeout, result.Kind);
        Assert.True(result.IsRetryable);
        Assert.Contains("timeout", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_IdleTimeout_ReturnsIdleTimeout()
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.Codex,
            TimedOut = true,
            IdleTimeout = true,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.IdleTimeout, result.Kind);
        Assert.True(result.IsRetryable);
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
            AgentId = AgentId.Codex,
            StderrLines = [stderrLine],
            ExitCode = 1,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.RateLimit, result.Kind);
        Assert.True(result.IsRetryable);
    }

    [Theory]
    [InlineData("auth failed")]
    [InlineData("please login to continue")]
    [InlineData("sign in required")]
    [InlineData("unauthorized access")]
    [InlineData("HTTP 401")]
    [InlineData("HTTP 403 Forbidden")]
    public void Analyze_AuthError_ReturnsAuthError(string stderrLine)
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.Codex,
            StderrLines = [stderrLine],
            ExitCode = 1,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.AuthError, result.Kind);
        Assert.False(result.IsRetryable);
    }

    [Theory]
    [InlineData("invalid model specified")]
    [InlineData("model not found")]
    [InlineData("model does not exist")]
    [InlineData("model not supported")]
    public void Analyze_InvalidModel_ReturnsInvalidModel(string stderrLine)
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.Codex,
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
            AgentId = AgentId.Codex,
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
            AgentId = AgentId.Codex,
            ExitCode = 137,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.ProcessCrash, result.Kind);
        Assert.True(result.IsRetryable);
        Assert.Contains("137", result.Reason);
    }

    [Fact]
    public void Analyze_NoSignals_ReturnsUnknown()
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.Codex,
            ExitCode = 0,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.Unknown, result.Kind);
        Assert.False(result.IsRetryable);
    }

    [Fact]
    public void Analyze_NullExitCode_ReturnsUnknown()
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.Codex,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.Unknown, result.Kind);
    }

    [Fact]
    public void Analyze_Timeout_TakesPriority_OverStderr()
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.Codex,
            TimedOut = true,
            StderrLines = ["rate limit exceeded"],
            ExitCode = 1,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.Timeout, result.Kind);
    }

    [Fact]
    public void Analyze_RateLimit_IncludesContextLines()
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.Codex,
            StderrLines = ["Error: rate limit exceeded", "retry after 60s"],
            ExitCode = 1,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.NotEmpty(result.ContextLines);
    }
}
