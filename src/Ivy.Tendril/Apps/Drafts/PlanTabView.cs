using Ivy.Tendril.Apps.Views.Sheets;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Drafts;

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
            // On mobile the content pane only has left padding, leaving the Markdown flush to the
            // right edge; add right padding on the mobile breakpoint to match.
            var planLayout = Layout.Vertical().Height(Size.Full())
                .Padding(new Responsive<Thickness?> { Mobile = new Thickness(0, 0, 2, 0) });
            if (selectedPlan.Status == PlanStatus.Failed)
                planLayout |= ContentView.BuildFailureCallout(selectedPlan);

            var annotatedContent = MarkdownHelper.AnnotateAllBrokenLinks(
                selectedPlan.LatestRevisionContent,
                planService.PlansDirectory);

            planLayout |= new Markdown(annotatedContent)
                .Article()
                .DangerouslyAllowLocalFiles()
                .OnLinkClick(FileSheet.CreateLinkClickHandler(openFileState, planId =>
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
