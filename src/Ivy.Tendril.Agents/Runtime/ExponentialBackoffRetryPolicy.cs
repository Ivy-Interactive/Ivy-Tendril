using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Runtime;

public sealed class ExponentialBackoffRetryPolicy(
    int maxAttempts = 3,
    TimeSpan? baseDelay = null,
    TimeSpan? maxDelay = null)
    : IRetryPolicy
{
    private readonly TimeSpan _baseDelay = baseDelay ?? TimeSpan.FromSeconds(2);
    private readonly TimeSpan _maxDelay = maxDelay ?? TimeSpan.FromSeconds(60);

    public RetryDecision ShouldRetry(RetryContext context)
    {
        if (context.Attempt >= maxAttempts || !context.Error.IsRetryable || context.Error.IsAuthError)
            return RetryDecision.No;

        var delay = TimeSpan.FromTicks(_baseDelay.Ticks * (1L << context.Attempt));
        if (delay > _maxDelay)
            delay = _maxDelay;

        return RetryDecision.After(delay);
    }
}
