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
    /// <remarks>
    ///     <para>
    ///         Deliberately <b>not</b> gated on the changed plan folder, unlike the detail views (see
    ///         <see cref="PlanRefreshGate" />). Every caller here renders a <em>list</em> of plans, and a
    ///         change to any plan can add it to that list, remove it from it, or reorder it - including a
    ///         plan the list is not currently showing, which is exactly the case a folder gate would
    ///         discard. Gating here would leave stale rows on screen.
    ///     </para>
    ///     <para>
    ///         The coalescer is what keeps that affordable: folder-blind means every event arrives, so a
    ///         burst must still cost one refresh (#2571).
    ///     </para>
    /// </remarks>
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
