namespace Ivy.Tendril.Apps.Plans.Dialogs;

/// <summary>
///     Warns before executing a plan that still has questions nobody answered.
///     <para>
///         Not a block: an unanswered question means "you decide", and ExecutePlan resolves one
///         itself by taking the <c>recommended</c> option. This is the confirmation that you meant
///         to let it.
///     </para>
/// </summary>
public class UnansweredQuestionsDialog(
    IState<bool> dialogOpen,
    int questionCount,
    Action onContinue) : ViewBase
{
    public override object? Build()
    {
        if (!dialogOpen.Value) return null;

        var noun = questionCount == 1 ? "question" : "questions";
        var verb = questionCount == 1 ? "it" : "them";

        return new Dialog(
            _ => dialogOpen.Set(false),
            new DialogHeader("Unanswered Questions"),
            new DialogBody(
                Text.P($"This plan has {questionCount} unanswered {noun}. Executing now leaves {verb} "
                       + "to the agent, which will take the recommended option where there is one and "
                       + "decide for itself where there is not.")
            ),
            new DialogFooter(
                Layout.Wrap().Gap(4, 2)
                    | new Button("Cancel").Outline().OnClick(() => dialogOpen.Set(false))
                    | new Button("Execute Anyway").Primary().OnClick(() =>
                    {
                        dialogOpen.Set(false);
                        onContinue();
                    })
            )
        ).Width(Size.Rem(32));
    }
}
