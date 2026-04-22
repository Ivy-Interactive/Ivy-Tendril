using Ivy.Tendril.Apps.Plans;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Apps.Review;

public class SidebarView(
    List<PlanFile> plans,
    IState<PlanFile?> selectedPlanState,
    IState<string?> projectFilter,
    IState<string?> levelFilter,
    IState<string?> textFilter,
    IState<bool> filtersOpen,
    IState<bool> showCompleted,
    IConfigService config) : ViewBase
{
    private readonly IConfigService _config = config;
    private readonly List<PlanFile> _plans = plans;
    private readonly IState<PlanFile?> _selectedPlanState = selectedPlanState;
    private readonly IState<string?> _projectFilter = projectFilter;
    private readonly IState<string?> _levelFilter = levelFilter;
    private readonly IState<string?> _textFilter = textFilter;
    private readonly IState<bool> _filtersOpen = filtersOpen;
    private readonly IState<bool> _showCompleted = showCompleted;

    private object BuildHeader()
    {
        var levelFilteredPlans = _plans.AsEnumerable();
        if (_levelFilter.Value is { } level)
            levelFilteredPlans = levelFilteredPlans.Where(p => p.Level == level);
        var projectCounts = levelFilteredPlans
            .GroupBy(p => p.Project)
            .OrderByDescending(g => g.Count())
            .Select(g => new Option<string>($"{g.Key} ({g.Count()})", g.Key))
            .ToArray<IAnyOption>();
        var levelOptions = _config.LevelNames;

        var searchInput = _textFilter.ToSearchInput()
            .Placeholder("Search...")
            .Suffix(
                new Button()
                    .Icon(_filtersOpen.Value ? Icons.ChevronUp : Icons.ChevronDown)
                    .Ghost()
                    .Small()
                    .OnClick(() => _filtersOpen.Set(!_filtersOpen.Value))
            );

        var header = Layout.Vertical() | searchInput;

        if (_filtersOpen.Value)
        {
            header |= Layout.Vertical()
                | _projectFilter.ToSelectInput(projectCounts).Placeholder("All Projects").Nullable()
                    .WithField().Label("Project")
                | _levelFilter.ToSelectInput(levelOptions.ToOptions()).Placeholder("All Levels").Nullable()
                    .WithField().Label("Level")
                | _showCompleted.ToBoolInput("Show Completed");
        }

        return header;
    }

    public override object Build()
    {
        var filteredPlans = PlanFilters.ApplyFilters(_plans, _projectFilter.Value, _levelFilter.Value, _textFilter.Value);

        var content = new List(filteredPlans.Select(plan =>
        {
            var clickablePlan = plan;
            var verificationsPassed = plan.Verifications.Count > 0
                                      && plan.Verifications.All(v => v.Status is "Pass" or "Skipped");

            return new ListItem($"#{plan.Id} {plan.Title}")
                .Content(Layout.Horizontal().Gap(1)
                         | new Badge(plan.Project).Variant(BadgeVariant.Outline).Small()
                             .WithProjectColor(_config, plan.Project)
                         | (verificationsPassed
                             ? new Badge("Verified").Variant(BadgeVariant.Success).Small()
                             : new Badge("Unverified").Variant(BadgeVariant.Warning).Small())
                )
                .OnClick(() => _selectedPlanState.Set(clickablePlan));
        }));

        return new HeaderLayout(BuildHeader(), content);
    }
}
