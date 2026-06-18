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
    IState<ImmutableList<MarkdownAnnotation>> annotations) : ViewBase
{
    public override object Build()
    {
        if (isEditing)
        {
            // The Plan tab is no longer wrapped in Cap(), so provide the scroll,
            // full height, and 1.5rem left inset (Padding(6,…)) here.
            return Layout.Vertical().Scroll(Scroll.Vertical).Width(Size.Full()).Height(Size.Full())
                | (Layout.Vertical()
                    .Padding(new Responsive<Thickness?> { Default = new Thickness(6, 0, 0, 4), Mobile = new Thickness(6, 4, 0, 4) })
                    .Width(Size.Full().Max(Size.Units(200)))
                    | editContentState.ToCodeInput()
                        .Language(Languages.Markdown)
                        .Width(Size.Full()));
        }
        else
        {
            var planLayout = Layout.Vertical().Height(Size.Full());
            if (selectedPlan.Status == PlanStatus.Failed)
                planLayout |= ContentView.BuildFailureCallout(selectedPlan);

            var annotatedContent = MarkdownHelper.AnnotateAllBrokenLinks(
                selectedPlan.LatestRevisionContent,
                planService.PlansDirectory);

            // Placeholder for the pinned interactive element. It lives in the widget's
            // StickyContent slot, which renders outside the markdown's scroll region, so
            // it stays in place while the plan content scrolls.
            var fixedElement = new Card(
                Layout.Vertical().Gap(2)
                | Text.Block("Actions").Bold()
                | new Button("Placeholder").Outline()
            ).Width(Size.Px(280));

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
                .StickyContent(fixedElement)
                .Annotations(annotations.Value)
                .OnAnnotationsChange(a => annotations.Set(a))
                .OnLinkClick(onLinkClick);

            return planLayout;
        }
    }
}
