using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Apps.Views;

namespace Ivy.Tendril.Apps.Icebox;

public class SidebarView(
    List<PlanFile> plans,
    IState<PlanFile?> selectedPlanState,
    IState<string?> projectFilter,
    IState<string?> levelFilter,
    IState<string?> textFilter,
    IState<bool> filtersOpen,
    IConfigService config) : ViewBase
{
    internal static LayoutView BuildRowBadges(PlanFile plan, IConfigService config)
    {
        var badges = Layout.Horizontal().Gap(1);

        foreach (var projectBadge in ProjectHelper.BuildBadges(plan.Project, config))
            badges |= projectBadge.Small();

        return badges
               | new Badge(plan.Level).Color(config.GetLevelColor(plan.Level) ?? Colors.Gray).Small();
    }

    internal static IAnyOption[] BuildProjectOptions(IEnumerable<PlanFile> plans, string? levelFilter)
    {
        var levelFilteredPlans = plans.AsEnumerable();
        if (levelFilter != null)
            levelFilteredPlans = levelFilteredPlans.Where(p => p.Level == levelFilter);

        return levelFilteredPlans
            .SelectMany(p => ProjectHelper.ParseProjects(p.Project))
            .GroupBy(name => name)
            .OrderByDescending(g => g.Count())
            .Select(g => new Option<string>($"{g.Key} ({g.Count()})", g.Key))
            .ToArray<IAnyOption>();
    }

    private object BuildHeader()
    {
        var projectCounts = BuildProjectOptions(plans, levelFilter.Value);
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
            return new HeaderLayout(BuildHeader(), new NoResultsView());
        }

        var content = new List(filteredList.Select(plan =>
        {
            var clickablePlan = plan;
            var badges = BuildRowBadges(plan, config);

            return SidebarListRow.Build($"#{plan.Id} {plan.Title}", badges, () => selectedPlanState.Set(clickablePlan),
                plan.FolderName == selectedPlanState.Value?.FolderName);
        }));

        return new HeaderLayout(BuildHeader(), content);
    }
}
