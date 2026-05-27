using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Runtime;

namespace Ivy.Tendril.Agents.Test.Runtime;

public class ExponentialBackoffRetryPolicyTests
{
    private static RetryContext MakeContext(int attempt, bool retryable = true, bool authError = false) => new()
    {
        Attempt = attempt,
        Error = new ErrorEvent
        {
            Kind = AgentEventKind.Error,
            Message = "test error",
            IsRetryable = retryable,
            IsAuthError = authError,
        },
        Elapsed = TimeSpan.FromSeconds(attempt * 5),
        AgentId = AgentId.Claude,
    };

    [Fact]
    public void ShouldRetry_FirstAttempt_ReturnsTrue()
    {
        var policy = new ExponentialBackoffRetryPolicy();

        var decision = policy.ShouldRetry(MakeContext(0));

        Assert.True(decision.ShouldRetry);
    }

    [Fact]
    public void ShouldRetry_FirstAttempt_DelayIsBaseDelay()
    {
        var policy = new ExponentialBackoffRetryPolicy(baseDelay: TimeSpan.FromSeconds(3));

        var decision = policy.ShouldRetry(MakeContext(0));

        Assert.Equal(TimeSpan.FromSeconds(3), decision.Delay);
    }

    [Fact]
    public void ShouldRetry_SecondAttempt_DelayIsDoubled()
    {
        var policy = new ExponentialBackoffRetryPolicy(baseDelay: TimeSpan.FromSeconds(2));

        var decision = policy.ShouldRetry(MakeContext(1));

        Assert.Equal(TimeSpan.FromSeconds(4), decision.Delay);
    }

    [Fact]
    public void ShouldRetry_ThirdAttempt_DelayIsQuadrupled()
    {
        var policy = new ExponentialBackoffRetryPolicy(baseDelay: TimeSpan.FromSeconds(2));

        var decision = policy.ShouldRetry(MakeContext(2));

        Assert.Equal(TimeSpan.FromSeconds(8), decision.Delay);
    }

    [Fact]
    public void ShouldRetry_ExceedsMaxAttempts_ReturnsNo()
    {
        var policy = new ExponentialBackoffRetryPolicy(maxAttempts: 3);

        var decision = policy.ShouldRetry(MakeContext(3));

        Assert.False(decision.ShouldRetry);
    }

    [Fact]
    public void ShouldRetry_NonRetryableError_ReturnsNo()
    {
        var policy = new ExponentialBackoffRetryPolicy();

        var decision = policy.ShouldRetry(MakeContext(0, retryable: false));

        Assert.False(decision.ShouldRetry);
    }

    [Fact]
    public void ShouldRetry_AuthError_ReturnsNo()
    {
        var policy = new ExponentialBackoffRetryPolicy();

        var decision = policy.ShouldRetry(MakeContext(0, authError: true));

        Assert.False(decision.ShouldRetry);
    }

    [Fact]
    public void ShouldRetry_DelayCappedAtMaxDelay()
    {
        var policy = new ExponentialBackoffRetryPolicy(
            maxAttempts: 20,
            baseDelay: TimeSpan.FromSeconds(10),
            maxDelay: TimeSpan.FromSeconds(30));

        var decision = policy.ShouldRetry(MakeContext(5));

        Assert.Equal(TimeSpan.FromSeconds(30), decision.Delay);
    }

    [Fact]
    public void ShouldRetry_DefaultMaxAttempts_IsThree()
    {
        var policy = new ExponentialBackoffRetryPolicy();

        Assert.True(policy.ShouldRetry(MakeContext(0)).ShouldRetry);
        Assert.True(policy.ShouldRetry(MakeContext(1)).ShouldRetry);
        Assert.True(policy.ShouldRetry(MakeContext(2)).ShouldRetry);
        Assert.False(policy.ShouldRetry(MakeContext(3)).ShouldRetry);
    }

    [Fact]
    public void ShouldRetry_DefaultBaseDelay_IsTwoSeconds()
    {
        var policy = new ExponentialBackoffRetryPolicy();

        var decision = policy.ShouldRetry(MakeContext(0));

        Assert.Equal(TimeSpan.FromSeconds(2), decision.Delay);
    }
}
