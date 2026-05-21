using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Gemini;

namespace Ivy.Tendril.Agents.Test.Gemini;

public class GeminiFailureAnalyzerTests
{
    private readonly GeminiFailureAnalyzer _analyzer = new();

    [Fact]
    public void Analyze_TimedOut_ReturnsTimeout()
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.Gemini,
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
            AgentId = AgentId.Gemini,
            TimedOut = true,
            IdleTimeout = true,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.IdleTimeout, result.Kind);
        Assert.True(result.IsRetryable);
        Assert.Contains("idle", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("quota exceeded")]
    [InlineData("RESOURCE_EXHAUSTED")]
    [InlineData("rate limit exceeded")]
    [InlineData("Error 429: too many requests")]
    public void Analyze_RateLimit_ReturnsRateLimit(string stderrLine)
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.Gemini,
            StderrLines = [stderrLine],
            ExitCode = 1,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.RateLimit, result.Kind);
        Assert.True(result.IsRetryable);
    }

    [Theory]
    [InlineData("oauth token expired")]
    [InlineData("auth failed")]
    [InlineData("unauthorized access")]
    [InlineData("HTTP 401")]
    [InlineData("HTTP 403 Forbidden")]
    public void Analyze_AuthError_ReturnsAuthError(string stderrLine)
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.Gemini,
            StderrLines = [stderrLine],
            ExitCode = 1,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.AuthError, result.Kind);
        Assert.False(result.IsRetryable);
    }

    [Fact]
    public void Analyze_AuthError_SuggestionContainsGeminiAuth()
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.Gemini,
            StderrLines = ["oauth token expired"],
            ExitCode = 1,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.NotNull(result.Suggestion);
        Assert.Contains("gemini auth", result.Suggestion, StringComparison.OrdinalIgnoreCase);
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
            AgentId = AgentId.Gemini,
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
            AgentId = AgentId.Gemini,
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
            AgentId = AgentId.Gemini,
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
            AgentId = AgentId.Gemini,
            ExitCode = 0,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.Unknown, result.Kind);
        Assert.False(result.IsRetryable);
    }

    [Fact]
    public void Analyze_NullExitCode_NoStderr_ReturnsUnknown()
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.Gemini,
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
            AgentId = AgentId.Gemini,
            TimedOut = true,
            StderrLines = ["RESOURCE_EXHAUSTED"],
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
            AgentId = AgentId.Gemini,
            StderrLines = ["Error: quota exceeded", "retry after 60s"],
            ExitCode = 1,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.NotEmpty(result.ContextLines);
    }
}
