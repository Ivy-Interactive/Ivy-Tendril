using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Apps.Views;

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
    private object BuildHeader()
    {
        var levelFilteredPlans = plans.AsEnumerable();
        if (levelFilter.Value is { } level)
            levelFilteredPlans = levelFilteredPlans.Where(p => p.Level == level);
        var projectCounts = levelFilteredPlans
            .GroupBy(p => p.Project)
            .OrderByDescending(g => g.Count())
            .Select(g => new Option<string>($"{g.Key} ({g.Count()})", g.Key))
            .ToArray<IAnyOption>();
        var levelOptions = config.LevelNames;

        var searchInput = textFilter.ToSearchInput()
            .Placeholder("Search")
            .Suffix(
                new Button()
                    .Icon(filtersOpen.Value ? Icons.ChevronUp : Icons.ChevronDown)
                    .Ghost()
                    .OnClick(() => filtersOpen.Set(!filtersOpen.Value))
            );

        var header = Layout.Vertical()
            | (Layout.Vertical().Height(Size.Px(40)).AlignContent(Align.Center) | searchInput);

        if (filtersOpen.Value)
        {
            header |= Layout.Vertical()
                | projectFilter.ToSelectInput(projectCounts).Placeholder("All Projects").Nullable()
                    .WithField().Label("Project")
                | levelFilter.ToSelectInput(levelOptions.ToOptions()).Placeholder("All Levels").Nullable()
                    .WithField().Label("Level")
                | showCompleted.ToBoolInput("Show Completed");
        }

        return header;
    }

    public override object Build()
    {
        var filteredPlans = PlanFilters.ApplyFilters(plans, projectFilter.Value, levelFilter.Value, textFilter.Value);

        var filteredList = filteredPlans.ToList();

        if (filteredList.Count == 0 && (projectFilter.Value != null || levelFilter.Value != null || !string.IsNullOrWhiteSpace(textFilter.Value)))
        {
            return new HeaderLayout(BuildHeader(), new NoResultsView());
        }

        var content = new List(filteredList.Select(plan =>
        {
            var clickablePlan = plan;
            var verificationsPassed = plan.Verifications.Count > 0
                                      && plan.Verifications.All(v => v.Status is "Pass" or "Skipped");

            return new ListItem($"#{plan.Id} {plan.Title}")
                .Content(Layout.Horizontal().Gap(1)
                         | new Badge(plan.Project).Variant(BadgeVariant.Outline).Small()
                             .WithProjectColor(config, plan.Project)
                         | (verificationsPassed
                             ? new Badge("Verified").Variant(BadgeVariant.Success).Small()
                             : new Badge("Unverified").Variant(BadgeVariant.Warning).Small())
                )
                .OnClick(() => selectedPlanState.Set(clickablePlan));
        }));

        return new HeaderLayout(BuildHeader(), content);
    }
}
