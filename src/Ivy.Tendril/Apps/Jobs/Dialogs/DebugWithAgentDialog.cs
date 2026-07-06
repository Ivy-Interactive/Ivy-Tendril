namespace Ivy.Tendril.Apps.Jobs.Dialogs;

public class DebugWithAgentDialog(
    IState<bool> isOpen,
    (string Label, Icons Icon) branding,
    Action<string?> onConfirm) : ViewBase
{
    public override object Build()
    {
        var focus = UseState("");

        void Confirm()
        {
            isOpen.Set(false);
            onConfirm(string.IsNullOrWhiteSpace(focus.Value) ? null : focus.Value.Trim());
        }

        return new Dialog(
            _ => isOpen.Set(false),
            new DialogHeader($"Debug with {branding.Label}"),
            new DialogBody(
                focus.ToTextareaInput("Anything particular you want to look for? (Optional)")
                    .Rows(3)
                    .AutoFocus()),
            new DialogFooter(
                new Button("Cancel").Ghost().OnClick(() => isOpen.Set(false)),
                new Button($"Debug with {branding.Label}").Primary().Icon(branding.Icon)
                    .ShortcutKey("Ctrl+Enter")
                    .OnClick(Confirm))
        ).Width(Size.Rem(32));
    }
}
