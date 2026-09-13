using System.Reactive.Disposables;
using Ivy.Tendril.AppShell;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Hooks;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Apps.Review;

[App(title: "Review", icon: Icons.ThumbsUp, group: ["Apps"], order: Constants.Review,
    allowDuplicateTabs: false)]
public class ReviewApp : ViewBase
{
    internal static List<ShellBadgeDto> BuildRowBadges(PlanFile plan)
    {
        var verificationsPassed = plan.Verifications.Count > 0
                                  && plan.Verifications.All(v => v.Status is VerificationStatus.Pass or VerificationStatus.Skipped);

        var badges = new List<ShellBadgeDto> { ShellBadgeDto.Project(plan.Project) };
        badges.Add(verificationsPassed
            ? ShellBadgeDto.Success("Verified")
            : ShellBadgeDto.Warning("Unverified"));
        // Completed over a failed gate: the deliverable may be missing (plan 00090).
        if (plan.PartialDelivery)
            badges.Add(ShellBadgeDto.Warning("Partial"));
        return badges;
    }

    internal static ShellSidebarListState BuildSidebarList(List<PlanFile> plans, PlanFile? selected)
    {
        var items = plans
            .Select(p => new ShellSectionItemDto(p.FolderName, p.Title, $"#{p.Id}", BuildRowBadges(p)))
            .ToList();
        return new ShellSidebarListState(
            "review", "Review", items, selected?.FolderName,
            planId => new ReviewAppArgs(planId));
    }

    public override object Build()
    {
        var planService = UseService<IPlanReaderService>();
        var jobService = UseService<IJobService>();
        var configService = UseService<IConfigService>();
        var gitService = UseService<IGitService>();
        var args = UseArgs<ReviewAppArgs>();
        var previousPlans = UseRef(new List<PlanFile>());
        var selectedFolderRef = UseRef<string?>(() =>
        {
            if (!string.IsNullOrEmpty(args?.PlanId))
            {
                return args.PlanId;
            }
            return null;
        });
        var selectedPlanState = UseState<PlanFile?>(() =>
        {
            if (!string.IsNullOrEmpty(args?.PlanId))
            {
                var p = planService.GetPlans().FirstOrDefault(x =>
                    x.FolderName.Equals(args.PlanId, StringComparison.OrdinalIgnoreCase) ||
                    x.Id.ToString() == args.PlanId ||
                    x.FolderName.StartsWith(args.PlanId + "-", StringComparison.OrdinalIgnoreCase));
                if (p != null)
                {
                    selectedFolderRef.Value = p.FolderName;
                    return p;
                }
            }
            return null;
        });
        var refreshToken = UseRefreshToken();
        var sidebarListSignal = Context.UseSignal<ShellSidebarListSignal, ShellSidebarListState, Unit>();

        Context.UseInboxAutoRefresh(refreshToken);

        UseEffect(() =>
        {
            if (!string.IsNullOrEmpty(args?.PlanId))
            {
                var p = planService.GetPlans().FirstOrDefault(x =>
                    x.FolderName.Equals(args.PlanId, StringComparison.OrdinalIgnoreCase) ||
                    x.Id.ToString() == args.PlanId ||
                    x.FolderName.StartsWith(args.PlanId + "-", StringComparison.OrdinalIgnoreCase));
                if (p != null && p.FolderName != selectedPlanState.Value?.FolderName)
                {
                    selectedFolderRef.Value = p.FolderName;
                    selectedPlanState.Set(p);
                }
            }
            return Disposable.Empty;
        });

        if (selectedPlanState.Value != null)
        {
            selectedFolderRef.Value = selectedPlanState.Value.FolderName;
        }

        var activePlanFolders = jobService.GetJobs()
            .Where(j => j.Status is JobStatus.Running or JobStatus.Queued or JobStatus.Pending or JobStatus.Blocked)
            .Select(j => j.TypedArgs?.PlanFolder)
            .Where(f => f != null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var plans = planService.GetPlans()
            .Where(p => p.Status is PlanStatus.Review or PlanStatus.Failed)
            .Where(p => !activePlanFolders.Contains(p.FolderPath))
            .OrderByDescending(p => p.Id)
            .ToList();

        var (resolvedPlan, resolvedFolder) = PlanSelectionHelper.ResolveSelection(
            selectedPlanState.Value,
            selectedFolderRef.Value,
            plans,
            previousPlans.Value,
            args?.PlanId);

        if (!ReferenceEquals(resolvedPlan, selectedPlanState.Value))
        {
            selectedPlanState.Set(resolvedPlan);
        }
        selectedFolderRef.Value = resolvedFolder;

        previousPlans.Value = plans;

        _ = sidebarListSignal.Send(BuildSidebarList(plans, selectedPlanState.Value));

        return new ContentView(selectedPlanState, plans, planService, jobService,
            RefreshPlans, configService, gitService);

        void RefreshPlans()
        {
            refreshToken.Refresh();
        }
    }
}
