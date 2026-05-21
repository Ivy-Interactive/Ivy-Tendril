using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Claude;

namespace Ivy.Tendril.Agents.Test.Claude;

public class ClaudeFailureAnalyzerTests
{
    private readonly ClaudeFailureAnalyzer _analyzer = new();

    [Fact]
    public void Analyze_TimedOut_ReturnsTimeout()
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.Claude,
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
            AgentId = AgentId.Claude,
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
            AgentId = AgentId.Claude,
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
            AgentId = AgentId.Claude,
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
    public void Analyze_InvalidModel_ReturnsInvalidModel(string stderrLine)
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.Claude,
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
            AgentId = AgentId.Claude,
            StderrLines = [stderrLine],
            ExitCode = 1,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.NetworkError, result.Kind);
        Assert.True(result.IsRetryable);
    }

    [Fact]
    public void Analyze_PermissionDenials_ReturnsPermissionBlocked()
    {
        var denial = new PermissionDenialEvent
        {
            Kind = AgentEventKind.PermissionDenial,
            ToolName = "Bash",
            InputSummary = "rm -rf /",
        };

        var resultEvent = new ResultEvent
        {
            Kind = AgentEventKind.Result,
            PermissionDenials = [denial],
        };

        var ctx = new FailureContext
        {
            Events = [resultEvent],
            AgentId = AgentId.Claude,
            ExitCode = 0,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.PermissionBlocked, result.Kind);
        Assert.False(result.IsRetryable);
        Assert.Contains("1 tool call", result.Reason);
    }

    [Fact]
    public void Analyze_NonZeroExitCode_ReturnsProcessCrash()
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.Claude,
            ExitCode = 137,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.ProcessCrash, result.Kind);
        Assert.True(result.IsRetryable);
        Assert.Contains("137", result.Reason);
    }

    [Fact]
    public void Analyze_ZeroExitNoIssues_ReturnsUnknown()
    {
        var ctx = new FailureContext
        {
            Events = [],
            AgentId = AgentId.Claude,
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
            AgentId = AgentId.Claude,
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
            AgentId = AgentId.Claude,
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
            AgentId = AgentId.Claude,
            StderrLines = ["Error: rate limit exceeded", "retry after 60s"],
            ExitCode = 1,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.NotEmpty(result.ContextLines);
    }

    [Fact]
    public void Analyze_MultiplePermissionDenials_CountsCorrectly()
    {
        var denials = new List<PermissionDenialEvent>
        {
            new() { Kind = AgentEventKind.PermissionDenial, ToolName = "Bash", InputSummary = "cmd1" },
            new() { Kind = AgentEventKind.PermissionDenial, ToolName = "Write", InputSummary = "cmd2" },
            new() { Kind = AgentEventKind.PermissionDenial, ToolName = "Edit", InputSummary = "cmd3" },
        };

        var resultEvent = new ResultEvent
        {
            Kind = AgentEventKind.Result,
            PermissionDenials = denials,
        };

        var ctx = new FailureContext
        {
            Events = [resultEvent],
            AgentId = AgentId.Claude,
            ExitCode = 0,
        };

        var result = _analyzer.Analyze(ctx);

        Assert.Equal(FailureKind.PermissionBlocked, result.Kind);
        Assert.Contains("3 tool call", result.Reason);
        Assert.Equal(3, result.ContextLines.Count);
    }
}
