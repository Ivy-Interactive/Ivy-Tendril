using Ivy.Tendril.Apps.Drafts;
using Ivy.Tendril.Apps.Icebox;
using Ivy.Tendril.Apps.Review;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Plans;

namespace Ivy.Tendril.AppShell.Dialogs;

/// <summary>
///     Full-text plan search over the plan database (FTS5 with LIKE fallback),
///     opened from the sidebar section's search icon. Selecting a result navigates
///     to the app that owns the plan's current status.
/// </summary>
public class PlanSearchDialog(IState<bool> dialogOpen) : ViewBase
{
    private const int MaxResults = 25;

    internal static (Type App, object? Args) ResolveTarget(PlanFile plan) => plan.Status switch
    {
        PlanStatus.Draft or PlanStatus.Blocked => (typeof(DraftsApp), new DraftsAppArgs(plan.FolderName)),
        PlanStatus.Icebox => (typeof(IceboxApp), null),
        _ => (typeof(ReviewApp), new ReviewAppArgs(plan.FolderName))
    };

    public override object Build()
    {
        var database = UseService<IPlanDatabaseService>();
        var configService = UseService<IConfigService>();
        var navigator = UseNavigation();
        var query = UseState("");

        var results = string.IsNullOrWhiteSpace(query.Value)
            ? []
            : database.SearchPlans(query.Value.Trim()).Take(MaxResults).ToList();

        object body = Layout.Vertical().Gap(2)
            | query.ToSearchInput().Placeholder("Search plans").Width(Size.Full());

        if (!string.IsNullOrWhiteSpace(query.Value))
        {
            if (results.Count == 0)
            {
                body = Layout.Vertical().Gap(2)
                    | query.ToSearchInput().Placeholder("Search plans").Width(Size.Full())
                    | Text.Muted("No plans found.");
            }
            else
            {
                var rows = new List(results.Select(plan =>
                {
                    var clickablePlan = plan;
                    var badges = Layout.Horizontal().Gap(1)
                        | new Badge(plan.Project).Variant(BadgeVariant.Outline).Small()
                            .WithProjectColor(configService, plan.Project)
                        | new Badge(plan.Status.ToString()).Variant(BadgeVariant.Secondary).Small();

                    return SidebarListRow.Build($"#{plan.Id} {plan.Title}", badges, () =>
                    {
                        dialogOpen.Set(false);
                        var (app, appArgs) = ResolveTarget(clickablePlan);
                        navigator.Navigate(app, appArgs);
                    });
                }));

                body = Layout.Vertical().Gap(2)
                    | query.ToSearchInput().Placeholder("Search plans").Width(Size.Full())
                    | rows;
            }
        }

        return new Dialog(
            _ => { dialogOpen.Set(false); return ValueTask.CompletedTask; },
            new DialogHeader("Search Plans"),
            new DialogBody(body)
        ).Width(Size.Px(560));
    }
}
