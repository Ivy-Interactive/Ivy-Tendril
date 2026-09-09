using System.Collections.Immutable;
using Ivy.Tendril.Apps.Views.Sheets;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Apps.Plans;

public class PlanTabView(
    PlanFile selectedPlan,
    IState<PlanFile?> selectedPlanState,
    bool isEditing,
    IState<string> editContentState,
    IState<string?> openFileState,
    IPlanReaderService planService,
    IConfigService config,
    IState<ImmutableList<MarkdownAnnotation>> annotations,
    IState<string> revisionContent,
    Action<QuestionAnswer> onAnswerChanged,
    QuestionScrollTarget? scrollTo,
    string? currentAuthor = null) : ViewBase
{
    public override object Build()
    {
        var draftAnnotationService = UseService<Ivy.Tendril.Services.Plans.IPlanAnnotationService>();

        if (isEditing)
        {
            // The Plan tab is not wrapped in Cap(), so provide the scroll, full height and the
            // workspace inset (24px top, 32px sides) here.
            return Layout.Vertical().Scroll(Scroll.Vertical).Width(Size.Full()).Height(Size.Full())
                | (Layout.Vertical()
                    .Padding(8, 6, 8, 4)
                    .Width(Size.Full().Max(Size.Units(200)))
                    | editContentState.ToCodeInput()
                        .Language(Languages.Markdown)
                        .Width(Size.Full()));
        }
        else
        {
            var planLayout = Layout.Vertical().Height(Size.Full());
            if (selectedPlan.Status == PlanStatus.Failed)
                planLayout |= ContentView.BuildFailureCallout(selectedPlan, config.TendrilHome);

            // Answers are merged into the raw revision, so display preparation happens after it —
            // otherwise the polished form would be what gets written back.
            var raw = revisionContent.Value;
            var annotatedContent = MarkdownHelper.PrepareForDisplay(raw, config);

            Action<string> onLinkClick = FileSheet.CreateLinkClickHandler(openFileState, planId =>
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

            planLayout |= new PlanMarkdown(annotatedContent)
                .Article()
                .DangerouslyAllowLocalFiles()
                .Height(Size.Full())
                .Annotations(annotations.Value)
                .CurrentAuthor(currentAuthor)
                .OnAnnotationsChange(a =>
                {
                    annotations.Set(a);
                    _ = draftAnnotationService.SaveAnnotationsAsync(selectedPlan.FolderPath, a);
                })
                .OnAnswersChange(onAnswerChanged)
                .ScrollTo(scrollTo)
                .OnLinkClick(onLinkClick);

            return planLayout;
        }
    }
}
