using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace Ivy.Tendril.Hooks;

public static class UseInboxAutoRefreshExtensions
{
    /// <summary>
    ///     Refreshes the view whenever plan/job state changes. Subscribes to the debounced
    ///     <see cref="ITendrilProcessStatusService.Status" /> observable (the same signal the
    ///     sidebar badges use, fed by PlansChanged, CountsInvalidated and JobsStructureChanged)
    ///     plus <see cref="IPlanWatcherService.PlansChanged" /> directly, which
    ///     covers content changes where the counts stay equal (Status dedupes by record equality).
    /// </summary>
    public static void UseInboxAutoRefresh(this IViewContext context, RefreshToken refreshToken)
    {
        var statusService = context.UseService<ITendrilProcessStatusService>();
        var planWatcher = context.UseService<IPlanWatcherService>();

        context.UseEffect(() =>
        {
            // Coalesce the two signals below (both fed by the same file/job events, ~300ms apart)
            // into a single refresh so a burst doesn't queue overlapping full re-renders.
            var coalescer = new RefreshCoalescer(refreshToken, TimeSpan.FromMilliseconds(400));

            // Skip(1): the BehaviorSubject replays the current value on subscribe.
            var subscription = statusService.Status.Skip(1).Subscribe(_ => coalescer.Request());

            void OnChanged(string? _)
            {
                coalescer.Request();
            }

            planWatcher.PlansChanged += OnChanged;
            return Disposable.Create(() =>
            {
                subscription.Dispose();
                planWatcher.PlansChanged -= OnChanged;
                coalescer.Dispose();
            });
        });
    }
}
