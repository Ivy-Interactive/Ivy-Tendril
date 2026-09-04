using System.Reactive.Disposables;
using Ivy.Tendril.AppShell;
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

    internal static List<ShellItemActionDto>? BuildRowActions(PlanFile plan, bool allowActions)
    {
        if (!allowActions || plan.Commits.Count == 0) return null;
        var label = plan.IsPullRequestSource ? "Update PR" : "Create PR";
        return [new ShellItemActionDto(ShellSidebarActions.CreatePr, label, "GitPullRequest", Primary: true)];
    }

    internal static ShellSidebarListState BuildSidebarList(List<PlanFile> plans, PlanFile? selected,
        Action<string, string>? onItemAction = null, bool allowActions = true)
    {
        var items = plans
            .Select(p => new ShellSectionItemDto(p.FolderName, p.Title, $"#{p.Id}", BuildRowBadges(p),
                BuildRowActions(p, allowActions)))
            .ToList();
        return new ShellSidebarListState(
            "review", "Review", items, selected?.FolderName,
            planId => new ReviewAppArgs(planId),
            OnItemAction: onItemAction);
    }

    public override object Build()
    {
        var planService = UseService<IPlanReaderService>();
        var jobService = UseService<IJobService>();
        var configService = UseService<IConfigService>();
        var gitService = UseService<IGitService>();
        var args = UseArgs<ReviewAppArgs>();
        var selectedPlanState = UseState<PlanFile?>(() =>
        {
            if (!string.IsNullOrEmpty(args?.PlanId))
            {
                return planService.GetPlans().FirstOrDefault(x =>
                    x.FolderName.Equals(args.PlanId, StringComparison.OrdinalIgnoreCase) ||
                    x.Id.ToString() == args.PlanId ||
                    x.FolderName.StartsWith(args.PlanId + "-", StringComparison.OrdinalIgnoreCase));
            }
            return null;
        });
        var refreshToken = UseRefreshToken();
        var sidebarListSignal = Context.UseSignal<ShellSidebarListSignal, ShellSidebarListState, Unit>();
        var shareContext = UseService<Ivy.Tendril.Services.Share.IShareContext>();
        var pendingSidebarAction = UseState<ShellSidebarActionRequest?>((ShellSidebarActionRequest?)null);

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
                    selectedPlanState.Set(p);
                }
            }
            return Disposable.Empty;
        });

        var previousPlans = UseRef(new List<PlanFile>());

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

        _ = sidebarListSignal.Send(BuildSidebarList(plans, selectedPlanState.Value,
            (itemId, actionId) => pendingSidebarAction.Set(new ShellSidebarActionRequest(itemId, actionId)),
            allowActions: !shareContext.IsShareMode));

        return new ContentView(selectedPlanState, plans, planService, jobService,
            RefreshPlans, configService, gitService, pendingSidebarAction);

        void RefreshPlans()
        {
            refreshToken.Refresh();
        }
    }
}
