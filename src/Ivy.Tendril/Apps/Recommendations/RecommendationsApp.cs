using Ivy.Tendril.AppShell;
using Ivy.Tendril.Hooks;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Apps.Recommendations;

[App(title: "Recommendations", icon: Icons.Lightbulb, group: ["Apps"], order: Constants.Recommendations)]
public class RecommendationsApp : ViewBase
{
    internal static string RecommendationId(Recommendation rec) => $"{rec.PlanId}::{rec.Title}";

    internal static List<ShellBadgeDto> BuildRowBadges(Recommendation rec)
    {
        // Mirror the detail header's badge row (Project + Impact) so each row is self-describing.
        var badges = new List<ShellBadgeDto> { ShellBadgeDto.Project(rec.Project) };
        if (rec.Impact is { } impact)
            badges.Add(impact switch
            {
                "High" => ShellBadgeDto.Success(impact),
                "Medium" => ShellBadgeDto.Warning(impact),
                _ => new ShellBadgeDto(impact)
            });
        return badges;
    }

    internal static ShellSidebarListState BuildSidebarList(List<Recommendation> recommendations, Recommendation? selected)
    {
        var items = recommendations
            .Select(r => new ShellSectionItemDto(RecommendationId(r), r.Title, $"#{r.ShortPlanId}", BuildRowBadges(r)))
            .ToList();
        return new ShellSidebarListState(
            "recommendations", "Recommendations", items,
            selected != null ? RecommendationId(selected) : null,
            id => new RecommendationsAppArgs(id));
    }

    public override object Build()
    {
        var planService = UseService<IPlanReaderService>();
        var jobService = UseService<IJobService>();
        var refreshToken = UseRefreshToken();
        var args = UseArgs<RecommendationsAppArgs>();
        var selectedState = UseState<Recommendation?>(null);
        var sidebarListSignal = Context.UseSignal<ShellSidebarListSignal, ShellSidebarListState, Unit>();

        Context.UseInboxAutoRefresh(refreshToken);

        var recommendations = planService.GetRecommendations();

        var allPending = recommendations
            .Where(r => r.State == RecommendationStatus.Pending && r.SourcePlanStatus == PlanStatus.Completed)
            .ToList();

        if (selectedState.Value == null && !string.IsNullOrEmpty(args?.RecommendationId))
        {
            var fromArgs = allPending.FirstOrDefault(r => RecommendationId(r) == args.RecommendationId);
            if (fromArgs != null) selectedState.Set(fromArgs);
        }

        if (selectedState.Value == null && allPending.Count > 0) selectedState.Set(allPending[0]);

        // If selected recommendation is no longer in the list, adjust selection
        if (selectedState.Value is { } selected &&
            !allPending.Any(r => r.PlanId == selected.PlanId && r.Title == selected.Title))
            selectedState.Set(allPending.Count > 0 ? allPending[0] : null);

        void Refresh()
        {
            refreshToken.Refresh();
        }

        _ = sidebarListSignal.Send(BuildSidebarList(allPending, selectedState.Value));

        return new ContentView(selectedState.Value, allPending, selectedState, planService, jobService, Refresh);
    }
}
