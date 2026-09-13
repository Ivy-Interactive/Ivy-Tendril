using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace Ivy.Tendril.Hooks;

/// <summary>
/// Collapses a burst of <see cref="Request"/> calls into at most one <see cref="RefreshToken.Refresh"/>
/// per <paramref name="window"/>. Uses <c>Sample</c> rather than <c>Throttle</c> so a refresh still
/// fires roughly every window under a sustained stream of requests, instead of being starved forever.
/// </summary>
internal sealed class RefreshCoalescer : IDisposable
{
    // Fully-qualified: the enclosing "Ivy" namespace also declares a "Unit" type, which shadows
    // System.Reactive.Unit for an unqualified name lookup here.
    private readonly Subject<System.Reactive.Unit> _requests = new();
    private readonly IDisposable _subscription;
    private volatile bool _disposed;

    public RefreshCoalescer(RefreshToken refreshToken, TimeSpan window, IScheduler? scheduler = null)
    {
        _subscription = _requests
            .Sample(window, scheduler ?? Scheduler.Default)
            .Subscribe(_ => refreshToken.Refresh());
    }

    /// <summary>
    ///     A request that arrives after <see cref="Dispose"/> is ignored rather than thrown at.
    ///
    ///     The events driving this are raised on timer threads against an invocation list
    ///     snapshotted before the owning view could unsubscribe, so a handler belonging to a view
    ///     that is going away can still reach here after its coalescer is gone — unsubscribing
    ///     first, as UseInboxAutoRefresh does, cannot close that window. The raiser catches and
    ///     logs what handlers throw, and that net is there to surface real subscriber bugs; a
    ///     routine teardown race running through it just makes a genuine one easier to miss.
    /// </summary>
    public void Request()
    {
        if (_disposed) return;
        _requests.OnNext(System.Reactive.Unit.Default);
    }

    public void Dispose()
    {
        _disposed = true;
        _subscription.Dispose();
        _requests.OnCompleted();
        // Deliberately not _requests.Dispose(). The flag above is read on threads other than the
        // one that sets it, so a request already past that check has to land somewhere harmless —
        // and OnNext on a completed subject is a no-op, where on a DISPOSED one it throws. The
        // subject holds nothing that needs releasing, and the line above has already severed the
        // only subscription.
    }
}
