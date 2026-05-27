using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.OpenCode;

namespace Ivy.Tendril.Agents.Test.OpenCode;

public class OpenCodeFailureAnalyzerTests
{
    private readonly OpenCodeFailureAnalyzer _analyzer = new();

    [Fact]
    public void Analyze_TimedOut_ReturnsTimeout()
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.OpenCode,
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
            AgentId = AgentId.OpenCode,
            TimedOut = true,
            IdleTimeout = true,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.IdleTimeout, result.Kind);
        Assert.True(result.IsRetryable);
        Assert.Contains("idle", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_ErrorEvent_AuthError_ReturnsAuthError_NotRetryable()
    {
        var errorEvent = new ErrorEvent
        {
            Kind = AgentEventKind.Error,
            Message = "unauthorized",
            IsRetryable = false,
            IsAuthError = true,
        };

        var ctx = new FailureContext
        {
            Events = [errorEvent],
            AgentId = AgentId.OpenCode,
            ExitCode = 0,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.AuthError, result.Kind);
        Assert.False(result.IsRetryable);
        Assert.Contains("opencode providers login", result.Suggestion!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_ErrorEvent_RetryableWithRateLimit_ReturnsRateLimit()
    {
        var errorEvent = new ErrorEvent
        {
            Kind = AgentEventKind.Error,
            Message = "rate limit exceeded",
            IsRetryable = true,
            IsAuthError = false,
        };

        var ctx = new FailureContext
        {
            Events = [errorEvent],
            AgentId = AgentId.OpenCode,
            ExitCode = 0,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.RateLimit, result.Kind);
        Assert.True(result.IsRetryable);
    }

    [Fact]
    public void Analyze_ErrorEvent_RetryableWith429_ReturnsRateLimit()
    {
        var errorEvent = new ErrorEvent
        {
            Kind = AgentEventKind.Error,
            Message = "Error 429: too many requests to the API",
            IsRetryable = true,
            IsAuthError = false,
        };

        var ctx = new FailureContext
        {
            Events = [errorEvent],
            AgentId = AgentId.OpenCode,
            ExitCode = 0,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.RateLimit, result.Kind);
        Assert.True(result.IsRetryable);
    }

    [Fact]
    public void Analyze_ErrorEvent_RetryableNonRateLimit_ReturnsNetworkError()
    {
        var errorEvent = new ErrorEvent
        {
            Kind = AgentEventKind.Error,
            Message = "connection timed out",
            IsRetryable = true,
            IsAuthError = false,
        };

        var ctx = new FailureContext
        {
            Events = [errorEvent],
            AgentId = AgentId.OpenCode,
            ExitCode = 0,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.NetworkError, result.Kind);
        Assert.True(result.IsRetryable);
    }

    [Fact]
    public void Analyze_ErrorEvent_NotRetryable_NotAuth_ReturnsUnknown()
    {
        var errorEvent = new ErrorEvent
        {
            Kind = AgentEventKind.Error,
            Message = "internal server error",
            IsRetryable = false,
            IsAuthError = false,
        };

        var ctx = new FailureContext
        {
            Events = [errorEvent],
            AgentId = AgentId.OpenCode,
            ExitCode = 0,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.Unknown, result.Kind);
        Assert.False(result.IsRetryable);
    }

    [Theory]
    [InlineData("rate limit exceeded")]
    [InlineData("Error 429")]
    [InlineData("Too Many Requests")]
    public void Analyze_NoErrorEvent_StderrRateLimit_ReturnsRateLimit(string stderrLine)
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.OpenCode,
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
    [InlineData("unauthorized access")]
    [InlineData("HTTP 401")]
    [InlineData("HTTP 403 Forbidden")]
    public void Analyze_NoErrorEvent_StderrAuth_ReturnsAuthError(string stderrLine)
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.OpenCode,
            StderrLines = [stderrLine],
            ExitCode = 1,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.AuthError, result.Kind);
        Assert.False(result.IsRetryable);
        Assert.Contains("opencode providers login", result.Suggestion!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("invalid model specified")]
    [InlineData("model not found")]
    [InlineData("model does not exist")]
    public void Analyze_NoErrorEvent_StderrModel_ReturnsInvalidModel(string stderrLine)
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.OpenCode,
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
    public void Analyze_NoErrorEvent_StderrNetwork_ReturnsNetworkError(string stderrLine)
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.OpenCode,
            StderrLines = [stderrLine],
            ExitCode = 1,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.NetworkError, result.Kind);
        Assert.True(result.IsRetryable);
    }

    [Fact]
    public void Analyze_NonZeroExitCode_NoOtherSignals_ReturnsProcessCrash()
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.OpenCode,
            ExitCode = 137,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.ProcessCrash, result.Kind);
        Assert.True(result.IsRetryable);
        Assert.Contains("137", result.Reason);
    }

    [Fact]
    public void Analyze_NoSignals_ReturnsUnknown_NotRetryable()
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.OpenCode,
            ExitCode = 0,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.Unknown, result.Kind);
        Assert.False(result.IsRetryable);
    }

    [Fact]
    public void Analyze_Timeout_TakesPriority_OverErrorEvents()
    {
        var errorEvent = new ErrorEvent
        {
            Kind = AgentEventKind.Error,
            Message = "rate limit exceeded",
            IsRetryable = true,
            IsAuthError = false,
        };

        var ctx = new FailureContext
        {
            Events = [errorEvent],
            AgentId = AgentId.OpenCode,
            TimedOut = true,
            StderrLines = ["rate limit exceeded"],
            ExitCode = 1,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.Timeout, result.Kind);
    }

    [Fact]
    public void Analyze_AuthError_SuggestionContainsProvidersLogin()
    {
        var errorEvent = new ErrorEvent
        {
            Kind = AgentEventKind.Error,
            Message = "unauthorized",
            IsRetryable = false,
            IsAuthError = true,
        };

        var ctx = new FailureContext
        {
            Events = [errorEvent],
            AgentId = AgentId.OpenCode,
            ExitCode = 0,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal("Run 'opencode providers login' to authenticate", result.Suggestion);
    }
}
