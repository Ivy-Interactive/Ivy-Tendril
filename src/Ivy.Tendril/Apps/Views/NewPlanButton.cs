using System.Runtime.InteropServices;

namespace Ivy.Tendril.Apps.Views;

public class NewPlanButton(bool collapsed = false) : ViewBase
{
    public static string GetTooltip(bool isMac) =>
        isMac ? "New Plan (⌘+⌥+N)" : "New Plan (Ctrl+Alt+N)";

    public static string GetTooltip() =>
        GetTooltip(RuntimeInformation.IsOSPlatform(OSPlatform.OSX));

    public override object Build()
    {
        return new CreatePlanDialogLauncher(
            open => collapsed
                ? new Button()
                    .Icon(Icons.Plus)
                    .Width(Size.Full())
                    .Variant(ButtonVariant.Primary)
                    .OnClick(open)
                    .Tooltip(GetTooltip())
                    .ShortcutKey("CTRL+ALT+N")
                : new Button("New Plan")
                    .Icon(Icons.Plus)
                    .Width(Size.Full())
                    .Variant(ButtonVariant.Primary)
                    .OnClick(open)
                    .ShortcutKey("CTRL+ALT+N"));
    }
}
