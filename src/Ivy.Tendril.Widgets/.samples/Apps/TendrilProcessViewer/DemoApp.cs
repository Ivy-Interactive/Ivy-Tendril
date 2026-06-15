using Ivy;
using Ivy.Tendril.Widgets;
using TendrilProcessViewerWidget = Ivy.Tendril.Widgets.TendrilProcessViewer;

namespace WidgetSamples.Apps.TendrilProcessViewer;

[App(title: "Process View", icon: Icons.Activity, group: ["TendrilProcessViewer"])]
class DemoApp : ViewBase
{
    public record ProcessViewModel(
        int DraftCount = 5,
        int ReviewCount = 6,
        int CreatingPlansCount = 1,
        int UpdatingPlansCount = 5,
        int ExecutingPlansCount = 5,
        int RetryingPlansCount = 3,
        int CreatingPrCount = 1
    );

    public override object Build()
    {
        var client = UseService<IClientProvider>();
        var model = UseState(() => new ProcessViewModel());

        var view = new TendrilProcessViewerWidget()
            .DraftCount(model.Value.DraftCount)
            .ReviewCount(model.Value.ReviewCount)
            .CreatingPlansCount(model.Value.CreatingPlansCount)
            .UpdatingPlansCount(model.Value.UpdatingPlansCount)
            .ExecutingPlansCount(model.Value.ExecutingPlansCount)
            .RetryingPlansCount(model.Value.RetryingPlansCount)
            .CreatingPrCount(model.Value.CreatingPrCount)
            .OnCreate(() => client.Toast("Create Plan clicked", "OnCreate").Info())
            .OnDrafts(() => client.Toast("Drafts clicked", "OnDrafts").Info())
            .OnReview(() => client.Toast("Review clicked", "OnReview").Info())
            .OnJobs(() => client.Toast("Jobs clicked", "OnJobs").Info());

        return new SidebarLayout(
            view,
            model.ToForm("Apply").SubmitStrategy(FormSubmitStrategy.OnChange)
        ).Resizable();
    }
}
