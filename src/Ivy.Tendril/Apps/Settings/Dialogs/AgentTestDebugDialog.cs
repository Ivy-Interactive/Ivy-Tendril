namespace Ivy.Tendril.Apps.Settings.Dialogs;

public class AgentTestDebugDialog(IState<string?> content) : ViewBase
{
    public override object? Build()
    {
        if (content.Value is null) return null;

        return new Dialog(
            _ => content.Set(null),
            new DialogHeader("Raw Output"),
            new DialogBody(Text.Code(content.Value)),
            new DialogFooter(new Button("Close").Outline().OnClick(() => content.Set(null)))
        ).Width(Size.Rem(50));
    }
}
