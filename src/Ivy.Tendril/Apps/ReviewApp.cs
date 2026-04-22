using System.Reactive.Disposables;
using Ivy.Tendril.Apps.Plans;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using ContentView = Ivy.Tendril.Apps.Review.ContentView;
using SidebarView = Ivy.Tendril.Apps.Review.SidebarView;

namespace Ivy.Tendril.Apps;

[App(title: "Review", icon: Icons.ThumbsUp, group: ["Apps"], order: MenuOrder.Review,
    allowDuplicateTabs: false)]
public class ReviewApp : ViewBase
{
    public override object Build()
    {
        var planService = UseService<IPlanReaderService>();
        var jobService = UseService<IJobService>();
        var configService = UseService<IConfigService>();
        var gitService = UseService<IGitService>();
        var planWatcher = UseService<IPlanWatcherService>();
        var selectedPlanState = UseState<PlanFile?>(null);
        var projectFilter = UseState<string?>(null);
        var levelFilter = UseState<string?>(null);
        var textFilter = UseState<string?>("");
        var showCompleted = UseState(false);
        var filtersOpen = UseState(false);
        var refreshToken = UseRefreshToken();

        UseEffect(() =>
        {
            void OnChanged(string? _)
            {
                refreshToken.Refresh();
            }

            planWatcher.PlansChanged += OnChanged;
            return Disposable.Create(() => planWatcher.PlansChanged -= OnChanged);
        });

        var previousPlans = UseRef(new List<PlanFile>());

        var plans = planService.GetPlans()
            .Where(p => showCompleted.Value
                ? p.Status is PlanStatus.ReadyForReview or PlanStatus.Failed or PlanStatus.Completed
                : p.Status is PlanStatus.ReadyForReview or PlanStatus.Failed)
            .ToList();
        var filteredPlans = PlanFilters.ApplyFilters(plans, projectFilter.Value, levelFilter.Value, textFilter.Value).ToList();

        if (selectedPlanState.Value == null && filteredPlans.Count > 0) selectedPlanState.Set(filteredPlans[0]);

        if (selectedPlanState.Value is { } selected && filteredPlans.All(p => p.FolderName != selected.FolderName))
        {
            var oldIndex = previousPlans.Value.FindIndex(p => p.FolderName == selected.FolderName);
            if (filteredPlans.Count > 0 && oldIndex >= 0)
            {
                var newIndex = Math.Min(oldIndex, filteredPlans.Count - 1);
                selectedPlanState.Set(filteredPlans[newIndex]);
            }
            else
            {
                selectedPlanState.Set(null);
            }
        }

        previousPlans.Value = filteredPlans;

        var levelFilteredPlans = plans.AsEnumerable();
        if (levelFilter.Value is { } level)
            levelFilteredPlans = levelFilteredPlans.Where(p => p.Level == level);
        var projectCounts = levelFilteredPlans
            .GroupBy(p => p.Project)
            .OrderByDescending(g => g.Count())
            .Select(g => new Option<string>($"{g.Key} ({g.Count()})", g.Key))
            .ToArray<IAnyOption>();
        var levelOptions = configService.LevelNames;

        var searchInput = textFilter.ToSearchInput()
            .Placeholder("Search...")
            .Suffix(
                new Button()
                    .Icon(filtersOpen.Value ? Icons.ChevronUp : Icons.ChevronDown)
                    .Ghost()
                    .Small()
                    .OnClick(() => filtersOpen.Set(!filtersOpen.Value))
            );
        var sidebarHeader = Layout.Vertical() | searchInput;
        if (filtersOpen.Value)
        {
            sidebarHeader |= Layout.Vertical()
                | projectFilter.ToSelectInput(projectCounts).Placeholder("All Projects").Nullable()
                    .WithField().Label("Project")
                | levelFilter.ToSelectInput(levelOptions.ToOptions()).Placeholder("All Levels").Nullable()
                    .WithField().Label("Level")
                | showCompleted.ToBoolInput("Show Completed");
        }

        var sidebar = new SidebarView(plans, selectedPlanState, projectFilter, levelFilter, textFilter, configService);

        return new SidebarLayout(
            new ContentView(selectedPlanState.Value, filteredPlans, selectedPlanState, planService, jobService,
                RefreshPlans, configService, gitService),
            sidebar,
            sidebarHeader: sidebarHeader
        );

        void RefreshPlans()
        {
            refreshToken.Refresh();
        }
    }
}
