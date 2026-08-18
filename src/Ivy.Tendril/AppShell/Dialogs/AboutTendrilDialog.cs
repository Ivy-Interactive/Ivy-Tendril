using Ivy.Tendril.Apps.Settings;

namespace Ivy.Tendril.AppShell.Dialogs;

public class AboutTendrilDialog(IState<bool> isOpen) : ViewBase
{
    public override object? Build()
    {
        if (!isOpen.Value) return null;

        void Close() => isOpen.Set(false);

        return new Dialog(
            _ => Close(),
            new DialogHeader("About Tendril"),
            new DialogBody(new AboutSetupView()),
            new DialogFooter(new Button("Close").Primary().OnClick(Close))
        ).Width(Size.Rem(40));
    }
}
