using System.Collections.Concurrent;

namespace Ivy.Tendril.Services.Jobs;

/// <summary>
///     Refuses to admit new jobs for a provider that has just proved it cannot serve them — the part of
///     the quota-wall problem that stops a queue draining into a known failure. A fleet of fourteen jobs
///     against an exhausted quota should cost one failure and one notification, not fourteen.
///     <para>
///         Deliberately narrow: retry, backoff, model fallback and per-provider concurrency limits are
///         issue #2705's, and live elsewhere. This type only answers "should the next job for this
///         provider start right now".
///     </para>
///     <para>
///         An instance, not a static, so a test (and a second <see cref="JobService" />) gets its own
///         state. Keyed by provider rather than by model: one quota is typically shared across a
///         provider's models, which is what the incident actually hit.
///     </para>
/// </summary>
internal sealed class ProviderCircuitBreaker
{
    /// <summary>
    ///     Consecutive provider failures before admission stops. Three, not one: a single failure can be
    ///     a one-off, and refusing to run on it would be worse than the bug.
    /// </summary>
    internal const int FailureThreshold = 3;

    /// <summary>
    ///     How long the breaker stays open before half-opening. A quota typically clears in seconds to
    ///     minutes, so a permanently open breaker would be worse than the wall it protects against: the
    ///     next job after the cooldown is admitted, and its own outcome re-trips or clears the breaker.
    /// </summary>
    internal static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(10);

    private sealed class ProviderState
    {
        public int ConsecutiveFailures;
        public DateTime? OpenedAt;
        public string? Reason;

        /// <summary>
        ///     Set when the trip has already been announced, so an open breaker raises exactly one
        ///     notification instead of one per refused job.
        /// </summary>
        public bool Notified;
    }

    private readonly ConcurrentDictionary<string, ProviderState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<DateTime> _now;

    internal ProviderCircuitBreaker(Func<DateTime>? now = null)
    {
        _now = now ?? (() => DateTime.UtcNow);
    }

    /// <summary>
    ///     Records a completed job that died of a provider failure. Returns true when this failure is the
    ///     one that trips the breaker <em>and</em> the trip has not been announced yet — i.e. exactly once
    ///     per open period, which is the caller's cue to notify.
    /// </summary>
    internal bool RecordFailure(string? provider, string message)
    {
        if (string.IsNullOrEmpty(provider))
            return false;

        var state = _states.GetOrAdd(provider, _ => new ProviderState());
        lock (state)
        {
            state.ConsecutiveFailures++;
            state.Reason = message;

            if (state.ConsecutiveFailures < FailureThreshold)
                return false;

            state.OpenedAt = _now();
            if (state.Notified)
                return false;

            state.Notified = true;
            return true;
        }
    }

    /// <summary>
    ///     Records a completed job for this provider that did <em>not</em> die of a provider failure. Any
    ///     such job is proof the provider is serving again, so the streak and the open state both clear.
    /// </summary>
    internal void RecordSuccess(string? provider)
    {
        if (string.IsNullOrEmpty(provider) || !_states.TryGetValue(provider, out var state))
            return;

        lock (state)
        {
            state.ConsecutiveFailures = 0;
            state.OpenedAt = null;
            state.Reason = null;
            state.Notified = false;
        }
    }

    /// <summary>
    ///     True while the breaker is open for this provider and still inside its cooldown. Reading it past
    ///     the cooldown half-opens the breaker: the caller admits the job, and its outcome decides.
    /// </summary>
    internal bool IsOpen(string? provider, out string? reason)
    {
        reason = null;
        if (string.IsNullOrEmpty(provider) || !_states.TryGetValue(provider, out var state))
            return false;

        lock (state)
        {
            if (state.OpenedAt is not { } openedAt)
                return false;

            if (_now() - openedAt >= Cooldown)
            {
                // Half-open: let one job through and judge the provider by what happens to it. The
                // streak is reset too, so a single failure after the cooldown does not re-trip
                // instantly on a count that predates it.
                state.OpenedAt = null;
                state.ConsecutiveFailures = 0;
                state.Notified = false;
                return false;
            }

            reason = string.IsNullOrEmpty(state.Reason)
                ? $"Waiting ({provider} provider unavailable)"
                : $"Waiting ({provider} provider unavailable: {state.Reason})";
            return true;
        }
    }
}
