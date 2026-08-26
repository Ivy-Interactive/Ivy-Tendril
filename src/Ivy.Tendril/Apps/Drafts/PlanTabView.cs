using System.Collections.Immutable;
using Ivy.Tendril.Apps.Views.Sheets;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Apps.Drafts;

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
    Action<QuestionAnswer> onAnswerChanged) : ViewBase
{
    public override object Build()
    {
        // Brings a question into view when its card entry is clicked. The token is what makes a
        // repeat click work — an unchanged id compares equal and nothing would move.
        var scrollTo = UseState<QuestionScrollTarget?>(() => null);

        if (isEditing)
        {
            // The Plan tab is no longer wrapped in Cap(), so provide the scroll,
            // full height, and 1.5rem left inset (Padding(6,…)) here.
            return Layout.Vertical().Scroll(Scroll.Vertical).Width(Size.Full()).Height(Size.Full())
                | (Layout.Vertical()
                    .Padding(6, 0, 0, 4)
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
            var questions = QuestionAnswers.Read(raw);

            var sticky = Layout.Vertical().Gap(4);
            if (questions.Count > 0)
            {
                sticky |= new QuestionsCardView(
                    questions,
                    id => scrollTo.Set(new QuestionScrollTarget(id, (scrollTo.Value?.Token ?? 0) + 1)));
            }

            sticky |= new VerificationsCardView(selectedPlan, planService, config);

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

            planLayout |= new DraftMarkdown(annotatedContent)
                .Article()
                .DangerouslyAllowLocalFiles()
                .Height(Size.Full())
                .StickyContent(sticky)
                .Annotations(annotations.Value)
                .OnAnnotationsChange(a => annotations.Set(a))
                .OnAnswersChange(onAnswerChanged)
                .ScrollTo(scrollTo.Value)
                .OnLinkClick(onLinkClick);

            return planLayout;
        }
    }
}
