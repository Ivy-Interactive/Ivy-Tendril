namespace Ivy.Tendril.Apps.Drafts.Dialogs;

/// <summary>
///     Warns before executing a plan that still carries work nobody has folded in: annotations, or
///     answers written into its <c>questions</c> blocks. Both are addressed by the same UpdatePlan
///     job, so they share one dialog.
///     <para>
///         The two are not discarded alike. Annotations live only in the UI, so declining to update
///         throws them away. Answers are already in the revision file, so they survive and the agent
///         honours them as written — which is why the decline button says nothing about discarding
///         when answers are involved.
///     </para>
/// </summary>
public class PendingAnnotationsDialog(
    IState<bool> dialogOpen,
    int annotationCount,
    int answeredQuestionCount,
    Action onUpdate,
    Action onUpdateAndExecute,
    Action onDiscardAndExecute) : ViewBase
{
    public override object? Build()
    {
        if (!dialogOpen.Value) return null;

        var hasAnnotations = annotationCount > 0;
        var hasAnswers = answeredQuestionCount > 0;

        var header = hasAnnotations && hasAnswers ? "Unincorporated Changes"
            : hasAnswers ? "Unincorporated Answers"
            : "Unincorporated Annotations";

        var declineLabel = hasAnnotations
            ? "Discard Annotations & Execute"
            : "Execute Without Updating";

        return new Dialog(
            _ => dialogOpen.Set(false),
            new DialogHeader(header),
            new DialogBody(Text.P(Message(annotationCount, answeredQuestionCount))),
            new DialogFooter(
                Layout.Wrap().Gap(4, 2)
                    | new Button("Cancel").Outline().OnClick(() => dialogOpen.Set(false))
                    | new Button("Update Plan").Outline().OnClick(() =>
                    {
                        dialogOpen.Set(false);
                        onUpdate();
                    })
                    | new Button(declineLabel).Outline().OnClick(() =>
                    {
                        dialogOpen.Set(false);
                        onDiscardAndExecute();
                    })
                    | new Button("Update Plan & Execute").Primary().OnClick(() =>
                    {
                        dialogOpen.Set(false);
                        onUpdateAndExecute();
                    })
            )
        ).Width(Size.Rem(32));
    }

    internal static string Message(int annotationCount, int answeredQuestionCount)
    {
        var annotations = $"{annotationCount} {(annotationCount == 1 ? "annotation" : "annotations")}";
        var answers = $"{answeredQuestionCount} answered "
                      + (answeredQuestionCount == 1 ? "question" : "questions");

        if (annotationCount > 0 && answeredQuestionCount > 0)
            return $"This plan has {annotations} and {answers} that haven't been incorporated yet. "
                   + "Executing now would ignore the annotations, and the agent would read the answers "
                   + "as they stand rather than as part of the plan.";

        if (answeredQuestionCount > 0)
            return $"This plan has {answers} that haven't been incorporated yet. Executing now means "
                   + "the agent reads them as they stand rather than as part of the plan.";

        return $"This plan has {annotations} that haven't been incorporated yet. Executing now would "
               + "ignore them.";
    }
}
