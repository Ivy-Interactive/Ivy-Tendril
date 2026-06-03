using System.Reactive.Disposables;
using Ivy.Tendril.Apps.Drafts;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Apps;

[App(title: "Drafts", icon: Icons.Feather, group: ["Apps"], order: Constants.Drafts)]
public class DraftsApp : ViewBase
{
    public override object Build()
    {
        var planService = UseService<IPlanReaderService>();
        var jobService = UseService<IJobService>();
        var configService = UseService<IConfigService>();
        var gitService = UseService<IGitService>();
        var planWatcher = UseService<IPlanWatcherService>();
        var args = UseArgs<DraftsAppArgs>();
        var nav = UseNavigation();
        var selectedPlanState = UseState<PlanFile?>(null);
        var projectFilter = UseState<string?>(null);
        var levelFilter = UseState<string?>(null);
        var textFilter = UseState<string?>("");
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

        UseEffect(() =>
        {
            if (!string.IsNullOrEmpty(args?.PlanId))
            {
                var p = planService.GetPlans().FirstOrDefault(x => x.FolderName == args.PlanId);
                if (p != null && p.FolderName != selectedPlanState.Value?.FolderName)
                {
                    selectedPlanState.Set(p);
                }
            }
            return Disposable.Empty;
        });

        UseEffect(() =>
        {
            if (selectedPlanState.Value != null && selectedPlanState.Value.FolderName != args?.PlanId)
            {
                nav.Navigate<DraftsApp>(new DraftsAppArgs(selectedPlanState.Value.FolderName));
            }
            return Disposable.Empty;
        });

        var previousPlans = UseRef(new List<PlanFile>());

        var activeJobs = jobService.GetJobs()
            .Where(j => j.Status is JobStatus.Running or JobStatus.Queued or JobStatus.Pending or JobStatus.Blocked)
            .ToList();

        var activePlanFolders = activeJobs
            .Select(j => j.TypedArgs?.PlanFolder)
            .Where(f => f != null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var activeCreatePlanIds = activeJobs
            .Where(j => j.TypedArgs is CreatePlanArgs)
            .Select(j => j.ReportedPlanId ?? j.AllocatedPlanId)
            .Where(id => id != null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var plans = planService.GetPlans()
            .Where(p => p.Status is PlanStatus.Draft or PlanStatus.Blocked)
            .Where(p => !activePlanFolders.Contains(p.FolderPath) &&
                        !activeCreatePlanIds.Any(id => p.FolderName.StartsWith(id + "-", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var filteredPlans = PlanFilters.ApplyFilters(plans, projectFilter.Value, levelFilter.Value, textFilter.Value)
            .ToList();

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

        var sidebar = new SidebarView(plans, selectedPlanState, projectFilter, levelFilter, textFilter, filtersOpen, configService);

        return new SidebarLayout(
            new ContentView(selectedPlanState.Value, filteredPlans, selectedPlanState, planService, jobService,
                RefreshPlans, configService, gitService),
            sidebar
        ).SidebarContentScroll(Scroll.None);

        void RefreshPlans()
        {
            refreshToken.Refresh();
        }
    }
}
