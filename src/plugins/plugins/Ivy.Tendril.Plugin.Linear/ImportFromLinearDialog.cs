namespace Ivy.Tendril.Plugin.Linear;

internal class ImportFromLinearDialog(IState<bool> dialogOpen, string apiKey) : ViewBase
{
    public override object? Build()
    {
        if (!dialogOpen.Value) return null;

        return new Dialog(
            _ => dialogOpen.Set(false),
            new DialogHeader("Import Issues from Linear"),
            new DialogBody(
                Text.Muted("Linear import coming soon.")
            ),
            new DialogFooter(
                new Button("Close").Outline().OnClick(() => dialogOpen.Set(false))
            )
        ).Width(Size.Rem(36));
    }
}
