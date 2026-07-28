namespace Ivy.Tendril.Apps.Views;

public class NewPlanButton(bool iconOnly = false) : ViewBase
{
    public override object Build()
    {
        return new CreatePlanDialogLauncher(
            open => (iconOnly
                    ? new Button(null).Icon(Icons.Plus).Tooltip("New Plan")
                    : new Button("New Plan").Icon(Icons.Plus).Width(Size.Full()))
                .Variant(ButtonVariant.Primary)
                .OnClick(open)
                .ShortcutKey("CTRL+ALT+N"));
    }
}
