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
    IState<bool> filtersOpen,
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
            .Placeholder("Search...")
            .Suffix(
                new Button()
                    .Icon(filtersOpen.Value ? Icons.ChevronUp : Icons.ChevronDown)
                    .Ghost()
                    .Small()
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
                    .WithField().Label("Level");
        }

        return header;
    }

    public override object Build()
    {
        var filteredPlans =
            PlanFilters.ApplyFilters(plans, projectFilter.Value, levelFilter.Value, textFilter.Value);

        var filteredList = filteredPlans.ToList();

        if (filteredList.Count == 0 && (projectFilter.Value != null || levelFilter.Value != null || !string.IsNullOrWhiteSpace(textFilter.Value)))
        {
            var emptyContent = Layout.Horizontal().Gap(2).AlignContent(Align.Center).Padding(4)
                   | new Icon(Icons.SearchX).Color(Colors.Gray)
                   | Text.Muted("No results. Try adjusting your filters.");
            return new HeaderLayout(BuildHeader(), emptyContent);
        }

        var content = new List(filteredList.Select(plan =>
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

        return new HeaderLayout(BuildHeader(), content);
    }
}
