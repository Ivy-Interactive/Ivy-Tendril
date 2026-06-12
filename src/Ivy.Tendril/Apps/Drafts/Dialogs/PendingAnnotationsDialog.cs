namespace Ivy.Tendril.Apps.Drafts.Dialogs;

public class PendingAnnotationsDialog(
    IState<bool> dialogOpen,
    int annotationCount,
    Action onUpdate,
    Action onUpdateAndExecute,
    Action onDiscardAndExecute) : ViewBase
{
    public override object? Build()
    {
        if (!dialogOpen.Value) return null;

        var noun = annotationCount == 1 ? "annotation" : "annotations";

        return new Dialog(
            _ => dialogOpen.Set(false),
            new DialogHeader("Unincorporated annotations"),
            new DialogBody(
                Text.P($"This plan has {annotationCount} {noun} that haven't been incorporated yet. Executing now would ignore them.")
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => dialogOpen.Set(false)),
                new Button("Discard & Execute").Outline().Foreground(Colors.Destructive).OnClick(() =>
                {
                    dialogOpen.Set(false);
                    onDiscardAndExecute();
                }),
                new Button("Update Plan").Outline().OnClick(() =>
                {
                    dialogOpen.Set(false);
                    onUpdate();
                }),
                new Button("Update Plan & Execute").Primary().OnClick(() =>
                {
                    dialogOpen.Set(false);
                    onUpdateAndExecute();
                })
            )
        ).Width(Size.Rem(32));
    }
}
