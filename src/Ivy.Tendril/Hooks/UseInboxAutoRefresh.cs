using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace Ivy.Tendril.Hooks;

public static class UseInboxAutoRefreshExtensions
{
    /// <summary>
    ///     Refreshes the view whenever plan/job/trash state changes. Subscribes to the debounced
    ///     <see cref="ITendrilProcessStatusService.Status" /> observable (the same signal the
    ///     sidebar badges use, fed by PlansChanged, CountsInvalidated, JobsStructureChanged and
    ///     the Trash watcher) plus <see cref="IPlanWatcherService.PlansChanged" /> directly, which
    ///     covers content changes where the counts stay equal (Status dedupes by record equality).
    /// </summary>
    public static void UseInboxAutoRefresh(this IViewContext context, RefreshToken refreshToken)
    {
        var statusService = context.UseService<ITendrilProcessStatusService>();
        var planWatcher = context.UseService<IPlanWatcherService>();

        context.UseEffect(() =>
        {
            // Skip(1): the BehaviorSubject replays the current value on subscribe.
            var subscription = statusService.Status.Skip(1).Subscribe(_ => refreshToken.Refresh());

            void OnChanged(string? _)
            {
                refreshToken.Refresh();
            }

            planWatcher.PlansChanged += OnChanged;
            return Disposable.Create(() =>
            {
                subscription.Dispose();
                planWatcher.PlansChanged -= OnChanged;
            });
        });
    }
}
