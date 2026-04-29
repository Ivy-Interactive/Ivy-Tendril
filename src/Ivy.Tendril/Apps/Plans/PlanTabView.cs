using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Ivy.Core;
using Ivy.Widgets.PlanAdjuster;
using PlanAdjusterWidget = Ivy.Widgets.PlanAdjuster.PlanAdjuster;

namespace Ivy.Tendril.Apps.Plans;

public class PlanTabView(
    PlanFile selectedPlan,
    IState<PlanFile?> selectedPlanState,
    bool isEditing,
    IState<string> editContentState,
    IState<string?> openFileState,
    IState<string> pendingAdjustmentsJson,
    IPlanReaderService planService) : ViewBase
{
    public override object Build()
    {
        if (isEditing)
        {
            return editContentState.ToCodeInput()
                .Language(Languages.Markdown)
                .Width(Size.Full());
        }

        var planLayout = Layout.Vertical().Height(Size.Full());
        if (selectedPlan.Status == PlanStatus.Failed)
            planLayout |= ContentView.BuildFailureCallout(selectedPlan);

        var annotatedContent = MarkdownHelper.AnnotateAllBrokenLinks(
            selectedPlan.LatestRevisionContent, planService.PlansDirectory);

        var linkClickHandler = FileLinkHelper.CreateFileLinkClickHandler(openFileState, planId =>
        {
            var planFolder = Directory.GetDirectories(planService.PlansDirectory, $"{planId:D5}-*")
                .FirstOrDefault();
            if (planFolder != null)
            {
                var plan = planService.GetPlanByFolder(planFolder);
                if (plan != null)
                    selectedPlanState.Set(plan);
            }
        });

        var supportsAdjustments = selectedPlan.Status is PlanStatus.Draft or PlanStatus.Blocked;
        if (supportsAdjustments)
        {
            planLayout |= new PlanAdjusterWidget()
                .Content(annotatedContent)
                .DangerouslyAllowLocalFiles()
                .OnLinkClick(linkClickHandler)
                .OnUpdate(json => pendingAdjustmentsJson.Set(json));
        }
        else
        {
            planLayout |= new Markdown(annotatedContent)
                .DangerouslyAllowLocalFiles()
                .OnLinkClick(linkClickHandler);
        }

        return planLayout;
    }
}
