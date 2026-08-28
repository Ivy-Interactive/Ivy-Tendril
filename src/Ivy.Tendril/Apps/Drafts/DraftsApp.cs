using System.Reactive.Disposables;
using Ivy.Tendril.AppShell;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Hooks;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Apps.Drafts;

[App(title: "Drafts", icon: Icons.Feather, group: ["Apps"], order: Constants.Drafts)]
public class DraftsApp : ViewBase
{
    internal static List<ShellBadgeDto> BuildRowBadges(PlanFile plan)
    {
        var badges = new List<ShellBadgeDto>();
        if (plan.Status != PlanStatus.Draft)
            badges.Add(ShellBadgeDto.Warning(plan.Status.ToString()));
        badges.AddRange(ProjectHelper.ParseProjects(plan.Project).Select(ShellBadgeDto.Project));
        if (!string.IsNullOrEmpty(plan.Level))
            badges.Add(new ShellBadgeDto(plan.Level));
        return badges;
    }

    internal static ShellSidebarListState BuildSidebarList(List<PlanFile> plans, PlanFile? selected)
    {
        var items = plans
            .Select(p => new ShellSectionItemDto(p.FolderName, p.Title, $"#{p.Id}", BuildRowBadges(p)))
            .ToList();
        return new ShellSidebarListState(
            "drafts", "Drafts", items, selected?.FolderName,
            planId => new DraftsAppArgs(planId));
    }

    public override object Build()
    {
        var planService = UseService<IPlanReaderService>();
        var jobService = UseService<IJobService>();
        var configService = UseService<IConfigService>();
        var gitService = UseService<IGitService>();
        var args = UseArgs<DraftsAppArgs>();
        var selectedPlanState = UseState<PlanFile?>(null);
        var refreshToken = UseRefreshToken();
        var sidebarListSignal = Context.UseSignal<ShellSidebarListSignal, ShellSidebarListState, Unit>();

        Context.UseInboxAutoRefresh(refreshToken);

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
            .OrderByDescending(p => p.Id)
            .ToList();

        // Only auto-select first plan if we didn't navigate here with specific args
        if (selectedPlanState.Value == null && plans.Count > 0 && string.IsNullOrEmpty(args?.PlanId))
        {
            selectedPlanState.Set(plans[0]);
        }

        if (selectedPlanState.Value is { } selected && plans.All(p => p.FolderName != selected.FolderName))
        {
            var oldIndex = previousPlans.Value.FindIndex(p => p.FolderName == selected.FolderName);

            if (plans.Count > 0 && oldIndex >= 0)
            {
                var newIndex = Math.Min(oldIndex, plans.Count - 1);
                selectedPlanState.Set(plans[newIndex]);
            }
            else
            {
                selectedPlanState.Set(null);
            }
        }

        previousPlans.Value = plans;

        _ = sidebarListSignal.Send(BuildSidebarList(plans, selectedPlanState.Value));

        return new ContentView(selectedPlanState.Value, plans, selectedPlanState, planService, jobService,
            RefreshPlans, configService, gitService);

        void RefreshPlans()
        {
            refreshToken.Refresh();
        }
    }
}
