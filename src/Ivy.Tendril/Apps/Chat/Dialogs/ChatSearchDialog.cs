using Ivy;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Apps.Chat.Dialogs;

/// <summary>
///     Search over chat titles, opened from the Chats sidebar section's search icon. Rows render
///     through the same ShellSidebarSection widget as the sidebar list, and picking one selects
///     that chat.
/// </summary>
public class ChatSearchDialog(
    IState<bool> dialogOpen,
    IChatHistoryService chatService,
    Action<string> selectSession) : ViewBase
{
    private const int MaxResults = 15;

    internal static ShellSidebarSection BuildResultsSection(
        IEnumerable<ChatSessionModel> sessions,
        Action<string> onSelect)
    {
        var items = sessions
            .Select(s => new ShellSectionItemDto(
                s.Id,
                ChatApp.DisplayTitle(s),
                s.UpdatedAt.ToLocalTime().ToString("MMM d"),
                Icon: s.IsTerminal() ? "Terminal" : null))
            .ToList();

        return new ShellSidebarSection()
            .Items(items)
            .Collapsible(false)
            .OnSelectItem(onSelect);
    }

    public override object Build()
    {
        var query = UseState("");

        var term = query.Value.Trim();
        var results = chatService.GetSessions()
            .Where(s => term.Length == 0 || ChatApp.DisplayTitle(s).Contains(term, StringComparison.OrdinalIgnoreCase))
            .Take(MaxResults)
            .ToList();

        var body = Layout.Vertical().Gap(2)
            | query.ToSearchInput().Placeholder("Search chats").Width(Size.Full());

        if (results.Count == 0)
        {
            body |= Text.Muted("No chats found.");
        }
        else
        {
            body |= BuildResultsSection(results, sessionId =>
            {
                dialogOpen.Set(false);
                selectSession(sessionId);
            });
        }

        return new Dialog(
            _ => { dialogOpen.Set(false); return ValueTask.CompletedTask; },
            new DialogHeader("Search Chats"),
            new DialogBody(body)
        ).Width(Size.Px(560));
    }
}
