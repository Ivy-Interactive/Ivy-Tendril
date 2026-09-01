using Ivy.Tendril.Apps.Plans;
using Ivy.Tendril.Apps.Icebox;
using Ivy.Tendril.Apps.Review;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Plans;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.AppShell.Dialogs;

/// <summary>
///     Full-text plan search over the plan database (FTS5 with LIKE fallback),
///     opened from the sidebar section's search icon. Rows render through the same
///     ShellSidebarSection widget and per-app badge builders as the sidebar lists,
///     so a plan looks identical here and there. Selecting a result navigates to
///     the app that owns the plan's current status.
/// </summary>
public class PlanSearchDialog(IState<bool> dialogOpen) : ViewBase
{
    private const int MaxResults = 15;

    internal static (Type App, object? Args) ResolveTarget(PlanFile plan) => plan.Status switch
    {
        PlanStatus.Draft or PlanStatus.Blocked => (typeof(PlansApp), new PlansAppArgs(plan.FolderName)),
        PlanStatus.Icebox => (typeof(IceboxApp), null),
        _ => (typeof(ReviewApp), new ReviewAppArgs(plan.FolderName))
    };

    /// <summary>Badges as the plan's owning sidebar list would show them; other statuses get a status badge.</summary>
    internal static List<ShellBadgeDto> BuildRowBadges(PlanFile plan) => plan.Status switch
    {
        PlanStatus.Review or PlanStatus.Failed => ReviewApp.BuildRowBadges(plan),
        PlanStatus.Draft or PlanStatus.Blocked => PlansApp.BuildRowBadges(plan),
        PlanStatus.Completed =>
            [ShellBadgeDto.Project(plan.Project), ShellBadgeDto.Success(plan.Status.ToString())],
        _ => [ShellBadgeDto.Project(plan.Project), new ShellBadgeDto(plan.Status.ToString())]
    };

    public override object Build()
    {
        var database = UseService<IPlanDatabaseService>();
        var navigator = UseNavigation();
        var query = UseState("");

        var results = string.IsNullOrWhiteSpace(query.Value)
            ? []
            : database.SearchPlans(query.Value.Trim()).Take(MaxResults).ToList();

        var body = Layout.Vertical().Gap(2)
            | query.ToSearchInput().Placeholder("Search plans").Width(Size.Full());

        if (!string.IsNullOrWhiteSpace(query.Value))
        {
            if (results.Count == 0)
            {
                body |= Text.Muted("No plans found.");
            }
            else
            {
                var resultsByFolder = results.ToDictionary(p => p.FolderName);
                var items = results
                    .Select(p => new ShellSectionItemDto(p.FolderName, p.Title, $"#{p.Id}", BuildRowBadges(p)))
                    .ToList();

                body |= new ShellSidebarSection()
                    .Items(items)
                    .OnSelectItem(folderName =>
                    {
                        if (!resultsByFolder.TryGetValue(folderName, out var plan)) return;
                        dialogOpen.Set(false);
                        var (app, appArgs) = ResolveTarget(plan);
                        navigator.Navigate(app, appArgs);
                    });
            }
        }

        return new Dialog(
            _ => { dialogOpen.Set(false); return ValueTask.CompletedTask; },
            new DialogHeader("Search Plans"),
            new DialogBody(body)
        ).Width(Size.Px(560));
    }
}
