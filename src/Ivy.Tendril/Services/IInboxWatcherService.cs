using Ivy.Tendril.Models;

namespace Ivy.Tendril.Services;

/// <summary>
/// Watches <c>TENDRIL_HOME/Inbox</c> and submits CreatePlan jobs for the files that appear there.
/// Master-only: the inbox is shared state, so a second instance sweeping it turns one dropped file
/// into several jobs.
/// </summary>
public interface IInboxWatcherService : IMasterOnlyStartable, IDisposable
{
    /// <summary>Raised once at the end of each recovery pass, including the passes that did nothing.</summary>
    event Action<InboxRecoverySummary>? RecoveryCompleted;

    /// <summary>
    /// The most recent recovery pass, or null if none has run yet. Recovery happens in <c>Start()</c>,
    /// which is before the app shell mounts, so a subscriber that arrives late reads this rather than
    /// missing the event.
    /// </summary>
    InboxRecoverySummary? LastRecovery { get; }
}
