using Ivy.Tendril.Apps.Plans;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Apps.Icebox;

public class SidebarView(
    List<PlanFile> plans,
    IState<PlanFile?> selectedPlanState,
    IState<string?> projectFilter,
    IState<string?> levelFilter,
    IState<string?> textFilter,
    IConfigService config) : ViewBase
{
    private readonly IConfigService _config = config;
    private readonly IState<string?> _levelFilter = levelFilter;
    private readonly List<PlanFile> _plans = plans;
    private readonly IState<string?> _projectFilter = projectFilter;
    private readonly IState<PlanFile?> _selectedPlanState = selectedPlanState;
    private readonly IState<string?> _textFilter = textFilter;

    public override object Build()
    {
        var filteredPlans =
            PlanFilters.ApplyFilters(_plans, _projectFilter.Value, _levelFilter.Value, _textFilter.Value);

        return new List(filteredPlans.Select(plan =>
        {
            var clickablePlan = plan;
            return new ListItem($"#{plan.Id} {plan.Title}")
                .Content(Layout.Horizontal().Gap(1)
                         | new Badge(plan.Project).Variant(BadgeVariant.Outline).Small()
                             .WithProjectColor(_config, plan.Project)
                         | new Badge(plan.Level).Variant(_config.GetBadgeVariant(plan.Level)).Small())
                .OnClick(() => _selectedPlanState.Set(clickablePlan));
        }));
    }
}
