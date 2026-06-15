using System.Reactive.Disposables;
using Ivy.Tendril.Apps;
using Ivy.Tendril.Apps.Drafts;
using Ivy.Tendril.Apps.Review;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Services;
using Ivy.Tendril.Widgets;
using JobsApp = Ivy.Tendril.Apps.Jobs.JobsApp;

namespace Ivy.Tendril.Hooks;

public static class UseTendrilProcessExtensions
{
    public static object UseTendrilProcess(this IViewContext context)
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
            new TendrilProcessViewer()
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
