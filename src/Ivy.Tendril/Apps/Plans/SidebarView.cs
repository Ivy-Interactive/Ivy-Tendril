using Ivy.Tendril.Models;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Plans;

public class SidebarView(
    List<PlanFile> plans,
    IState<PlanFile?> selectedPlanState,
    IState<string?> projectFilter,
    IState<string?> levelFilter,
    IState<string?> textFilter,
    IConfigService config) : ViewBase
{
    public override object Build()
    {
        var filteredPlans =
            PlanFilters.ApplyFilters(plans, projectFilter.Value, levelFilter.Value, textFilter.Value);

        return new List(filteredPlans.Select(plan =>
        {
            var clickablePlan = plan;
            var stateBadgeVariant = StatusMappings.PlanStatusBadgeVariants.TryGetValue(plan.Status, out var variant)
                ? variant
                : BadgeVariant.Outline;

            var badges = Layout.Horizontal().Gap(1);
            if (plan.Status != PlanStatus.Draft)
                badges |= new Badge(plan.Status.ToString()).Variant(stateBadgeVariant).Small();
            var projects = ProjectHelper.ParseProjects(plan.Project);
            foreach (var proj in projects)
            {
                badges |= new Badge(proj).Variant(BadgeVariant.Outline).Small()
                    .WithProjectColor(config, proj);
            }
            badges |= new Badge(plan.Level).Variant(config.GetBadgeVariant(plan.Level)).Small();

            return new ListItem($"#{plan.Id} {plan.Title}")
                .Content(badges)
                .OnClick(() => selectedPlanState.Set(clickablePlan));
        }));
    }
}
