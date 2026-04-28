using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Ivy.Core;

namespace Ivy.Tendril.Apps.Plans;

public class PlanTabView(
    PlanFile selectedPlan,
    IState<PlanFile?> selectedPlanState,
    bool isEditing,
    IState<string> editContentState,
    IState<string?> openFileState,
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
        else
        {
            var planLayout = Layout.Vertical().Height(Size.Full());
            if (selectedPlan.Status == PlanStatus.Failed)
                planLayout |= ContentView.BuildFailureCallout(selectedPlan);

            var annotatedContent = MarkdownHelper.AnnotateAllBrokenLinks(
                selectedPlan.LatestRevisionContent,
                planService.PlansDirectory);

            planLayout |= new Markdown(annotatedContent)
                .DangerouslyAllowLocalFiles()
                .OnLinkClick(FileLinkHelper.CreateFileLinkClickHandler(openFileState, planId =>
                {
                    var planFolder = Directory.GetDirectories(planService.PlansDirectory, $"{planId:D5}-*")
                        .FirstOrDefault();
                    if (planFolder != null)
                    {
                        var plan = planService.GetPlanByFolder(planFolder);
                        if (plan != null)
                            selectedPlanState.Set(plan);
                    }
                }));

            return planLayout;
        }
    }
}
