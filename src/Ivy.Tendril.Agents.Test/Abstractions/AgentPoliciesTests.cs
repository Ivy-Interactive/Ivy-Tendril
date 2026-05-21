using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Test.Abstractions;

public class AgentPoliciesTests
{
    [Fact]
    public void RetryDecision_No_ShouldNotRetry()
    {
        var decision = RetryDecision.No;
        Assert.False(decision.ShouldRetry);
    }

    [Fact]
    public void RetryDecision_After_ShouldRetryWithDelay()
    {
        var decision = RetryDecision.After(TimeSpan.FromSeconds(5));
        Assert.True(decision.ShouldRetry);
        Assert.Equal(TimeSpan.FromSeconds(5), decision.Delay);
    }

    [Fact]
    public void TimeoutPolicy_Default_HasReasonableValues()
    {
        var policy = TimeoutPolicy.Default;
        Assert.Equal(TimeSpan.FromMinutes(30), policy.TotalTimeout);
        Assert.Equal(TimeSpan.FromMinutes(5), policy.IdleTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), policy.StartupTimeout);
    }

    [Fact]
    public void SessionMetadata_DefaultTags_IsEmpty()
    {
        var meta = new SessionMetadata();
        Assert.Empty(meta.Tags);
    }

    [Fact]
    public void SessionMetadata_WithAllFields_RoundTrips()
    {
        var meta = new SessionMetadata
        {
            JobId = "job-123",
            TriggeredBy = "user@example.com",
            ProjectId = "proj-abc",
            Branch = "feature/test",
            Tags = new Dictionary<string, string> { ["env"] = "staging" },
        };

        Assert.Equal("job-123", meta.JobId);
        Assert.Equal("user@example.com", meta.TriggeredBy);
        Assert.Equal("proj-abc", meta.ProjectId);
        Assert.Equal("feature/test", meta.Branch);
        Assert.Equal("staging", meta.Tags["env"]);
    }

    [Fact]
    public void ConcurrencyOptions_DefaultMaxConcurrency_IsFour()
    {
        var opts = new ConcurrencyOptions();
        Assert.Equal(4, opts.MaxConcurrency);
        Assert.Null(opts.QueueTimeout);
    }

    [Fact]
    public void RetryContext_CreatesWithRequiredFields()
    {
        var ctx = new RetryContext
        {
            Error = new ErrorEvent { Kind = AgentEventKind.Error, Message = "rate limit" },
            Attempt = 2,
            Elapsed = TimeSpan.FromSeconds(10),
            AgentId = AgentId.Claude,
        };

        Assert.Equal(2, ctx.Attempt);
        Assert.Equal(AgentId.Claude, ctx.AgentId);
    }
}
