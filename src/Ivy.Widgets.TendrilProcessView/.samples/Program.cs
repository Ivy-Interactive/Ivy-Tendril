using Ivy;
using Ivy.Widget.TendrilProcessView;

var server = new Server();
server
    .UseHotReload()
    .AddApp<TendrilProcessViewDemo>();
await server.RunAsync();

[App]
class TendrilProcessViewDemo : ViewBase
{
    public record ProcessViewModel(
        int DraftCount = 5,
        int ReviewCount = 6,
        int CreatingPlansCount = 1,
        int UpdatingPlansCount = 5,
        int ExecutingPlansCount = 5,
        int RetryingPlansCount = 3
    );

    public override object Build()
    {
        var client = UseService<IClientProvider>();
        var model = UseState(() => new ProcessViewModel());

        return Layout.Horizontal()
            | (Layout.Vertical().Width(Size.Grow())
                | new TendrilProcessView()
                    .DraftCount(model.Value.DraftCount)
                    .ReviewCount(model.Value.ReviewCount)
                    .CreatingPlansCount(model.Value.CreatingPlansCount)
                    .UpdatingPlansCount(model.Value.UpdatingPlansCount)
                    .ExecutingPlansCount(model.Value.ExecutingPlansCount)
                    .RetryingPlansCount(model.Value.RetryingPlansCount)
                    .OnCreate(() => client.Toast("Create Plan clicked", "OnCreate").Info())
                    .OnDrafts(() => client.Toast("Drafts clicked", "OnDrafts").Info())
                    .OnReview(() => client.Toast("Review clicked", "OnReview").Info())
                    .OnJobs(() => client.Toast("Jobs clicked", "OnJobs").Info()))
            | (Layout.Vertical().Width(Size.Units(80))
                | model.ToForm());
    }
}
