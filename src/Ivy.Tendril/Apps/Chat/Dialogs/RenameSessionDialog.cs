using Ivy;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Chat.Dialogs;

public class RenameSessionDialog(
    IState<string?> renamingSessionId,
    IState<string> renameText,
    IChatHistoryService chatService,
    IState<int> sessionVersion) : ViewBase
{
    public override object? Build()
    {
        if (renamingSessionId.Value == null) return null;

        return new Dialog(
            _ => renamingSessionId.Set(null),
            new DialogHeader("Rename Session"),
            new DialogBody(
                renameText.ToTextInput("Session title").AutoFocus()
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => renamingSessionId.Set(null)),
                new Button("Save").Primary().OnClick(() =>
                {
                    if (!string.IsNullOrWhiteSpace(renameText.Value) && renamingSessionId.Value != null)
                    {
                        chatService.RenameSession(renamingSessionId.Value, renameText.Value.Trim());
                        sessionVersion.Set(v => v + 1);
                    }
                    renamingSessionId.Set(null);
                })
            )
        ).Width(Size.Rem(28));
    }
}
