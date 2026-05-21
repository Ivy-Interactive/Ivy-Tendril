namespace Ivy.Tendril.Agents.Abstractions;

public interface IRetryPolicy
{
    RetryDecision ShouldRetry(RetryContext context);
}

public sealed record RetryContext
{
    public required ErrorEvent Error { get; init; }
    public required int Attempt { get; init; }
    public required TimeSpan Elapsed { get; init; }
    public required string AgentId { get; init; }
}

public sealed record RetryDecision
{
    public required bool ShouldRetry { get; init; }
    public TimeSpan Delay { get; init; }
    public string? FallbackAgentId { get; init; }

    public static RetryDecision No { get; } = new() { ShouldRetry = false };
    public static RetryDecision After(TimeSpan delay) => new() { ShouldRetry = true, Delay = delay };
}

public sealed record TimeoutPolicy
{
    public TimeSpan? TotalTimeout { get; init; }
    public TimeSpan? IdleTimeout { get; init; }
    public TimeSpan? StartupTimeout { get; init; }

    public static TimeoutPolicy Default { get; } = new()
    {
        TotalTimeout = TimeSpan.FromMinutes(30),
        IdleTimeout = TimeSpan.FromMinutes(5),
        StartupTimeout = TimeSpan.FromSeconds(30),
    };
}

public sealed record SessionMetadata
{
    public string? JobId { get; init; }
    public string? TriggeredBy { get; init; }
    public string? ProjectId { get; init; }
    public string? Branch { get; init; }
    public IReadOnlyDictionary<string, string> Tags { get; init; } = new Dictionary<string, string>();
}

public sealed record ConcurrencyOptions
{
    public int MaxConcurrency { get; init; } = 4;
    public TimeSpan? QueueTimeout { get; init; }
}
