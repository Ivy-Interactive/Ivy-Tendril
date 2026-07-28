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

    public RefreshCoalescer(RefreshToken refreshToken, TimeSpan window, IScheduler? scheduler = null)
    {
        _subscription = _requests
            .Sample(window, scheduler ?? Scheduler.Default)
            .Subscribe(_ => refreshToken.Refresh());
    }

    public void Request() => _requests.OnNext(System.Reactive.Unit.Default);

    public void Dispose()
    {
        _subscription.Dispose();
        _requests.OnCompleted();
        _requests.Dispose();
    }
}
