using Ivy.Tendril.Apps.Drafts;
using Ivy.Tendril.Apps.Review;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Plans;

namespace Ivy.Tendril.Hooks;

public static class UsePlanNavigationExtensions
{
    public static Action<int> UsePlanNavigation(
        this IViewContext context,
        IPlanReaderService planService,
        Action<string> showPlanSheet)
    {
        var nav = context.UseNavigation();

        return planId =>
        {
            var planFolder = Directory.GetDirectories(planService.PlansDirectory, $"{planId:D5}-*")
                .FirstOrDefault();
            if (planFolder == null) return;

            var plan = planService.GetPlanByFolder(planFolder);
            if (plan == null)
            {
                showPlanSheet(planFolder);
                return;
            }

            if (plan.Status is PlanStatus.Draft or PlanStatus.Blocked)
                nav.Navigate<DraftsApp>(new DraftsAppArgs(plan.FolderName));
            else if (plan.Status is PlanStatus.Review or PlanStatus.Failed)
                nav.Navigate<ReviewApp>(new ReviewAppArgs(plan.FolderName));
            else
                showPlanSheet(planFolder);
        };
    }
}
