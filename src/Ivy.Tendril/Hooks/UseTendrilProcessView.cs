using System.Reactive.Disposables;
using Ivy.Tendril.Apps;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Services;
using Ivy.Widgets.TendrilProcessView;

namespace Ivy.Tendril.Hooks;

public static class UseTendrilProcessViewExtensions
{
    public static object UseTendrilProcessView(this IViewContext context)
    {
        var statusService = context.UseService<ITendrilProcessStatusService>();
        var navigator = context.UseNavigation();
        var processStatus = context.UseState(() => statusService.Current);

        context.UseEffect(() =>
        {
            var subscription = statusService.Status.Subscribe(s => processStatus.Set(s));
            return Disposable.Create(() => subscription.Dispose());
        });

        var status = processStatus.Value;
        return new CreatePlanDialogLauncher(open =>
            new TendrilProcessView()
                .DraftCount(status.DraftCount)
                .ReviewCount(status.ReviewCount)
                .CreatingPlansCount(status.CreatingPlansCount)
                .UpdatingPlansCount(status.UpdatingPlansCount)
                .ExecutingPlansCount(status.ExecutingPlansCount)
                .RetryingPlansCount(status.RetryingPlansCount)
                .CreatingPrCount(status.CreatingPrCount)
                .OnCreate(open)
                .OnDrafts(() => navigator.Navigate<DraftsApp>())
                .OnReview(() => navigator.Navigate<ReviewApp>())
                .OnJobs(() => navigator.Navigate<JobsApp>())
        );
    }
}
