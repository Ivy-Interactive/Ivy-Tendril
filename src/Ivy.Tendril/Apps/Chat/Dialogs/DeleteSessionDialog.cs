using Ivy;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Chat.Dialogs;

public class DeleteSessionDialog(
    IState<string?> deletingSessionId,
    ChatSessionModel? session,
    IChatHistoryService chatService,
    IState<string?> activeSessionId,
    IState<int> sessionVersion) : ViewBase
{
    public override object? Build()
    {
        if (deletingSessionId.Value == null) return null;

        var sessionTitle = !string.IsNullOrWhiteSpace(session?.Title)
            ? $"\"{session.Title}\""
            : "this chat session";

        return new Dialog(
            _ => deletingSessionId.Set(null),
            new DialogHeader("Delete Session"),
            new DialogBody(
                Text.P($"Are you sure you want to delete {sessionTitle}? This action cannot be undone.")
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => deletingSessionId.Set(null)),
                new Button("Delete").Destructive().ShortcutKey("Enter").AutoFocus().OnClick(() =>
                {
                    var idToDelete = deletingSessionId.Value;
                    if (idToDelete != null)
                    {
                        chatService.DeleteSession(idToDelete);
                        sessionVersion.Set(v => v + 1);
                        if (activeSessionId.Value == idToDelete)
                        {
                            var remaining = chatService.GetSessions();
                            activeSessionId.Set(remaining.FirstOrDefault()?.Id);
                        }
                    }
                    deletingSessionId.Set(null);
                })
            )
        ).Width(Size.Rem(28));
    }
}
