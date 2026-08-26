using Ivy.Tendril.Widgets;
using Ivy.Tendril.Apps.Views.Sheets;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Review.Tabs;

/// <summary>
///     The read-only "Plan" tab in the Review app: renders the plan's latest revision as
///     article markdown, with local files allowed and links routed through the FileSheet
///     handler (a "#planId" link switches the selected plan instead of opening a file).
/// </summary>
public class PlanTabView(
    PlanFile selectedPlan,
    IState<PlanFile?> selectedPlanState,
    IState<string?> openFile,
    IPlanReaderService planService,
    IConfigService config) : ViewBase
{
    public override object Build()
    {
        var annotated = MarkdownHelper.PrepareForDisplay(selectedPlan.LatestRevisionContent, config);

        // DraftMarkdown, but with neither OnAnnotationsChange nor OnAnswersChange: a plan under
        // review is read, not edited. Not subscribing is what makes its `questions` blocks present
        // the decisions instead of a picker — the plain Markdown widget would show raw YAML.
        return Layout.Vertical().Height(Size.Full())
               | new DraftMarkdown(annotated)
                   .DangerouslyAllowLocalFiles()
                   .Article()
                   .Height(Size.Full())
                   .OnLinkClick(FileSheet.CreateLinkClickHandler(openFile, planId =>
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
    }
}
