namespace Ivy.Tendril.Apps.Views;

public class NewPlanButton(bool collapsed = false) : ViewBase
{
    public override object Build()
    {
        return new CreatePlanDialogLauncher(
            open => collapsed
                ? new Button()
                    .Icon(Icons.Plus)
                    .Width(Size.Full())
                    .Variant(ButtonVariant.Primary)
                    .OnClick(open)
                    .Tooltip("New Plan (Ctrl+Alt+N)")
                    .ShortcutKey("CTRL+ALT+N")
                : new Button("New Plan")
                    .Icon(Icons.Plus)
                    .Width(Size.Full())
                    .Variant(ButtonVariant.Primary)
                    .OnClick(open)
                    .ShortcutKey("CTRL+ALT+N"));
    }
}
