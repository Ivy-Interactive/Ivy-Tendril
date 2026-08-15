using System;
using System.Collections.Generic;
using System.Linq;
using Ivy;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Chat;

public class SidebarView(
    IReadOnlyList<ChatSessionModel> sessions,
    IState<string?> activeSessionId,
    IState<int> sessionVersion,
    IState<string> selectedAgent,
    IState<string> selectedModel,
    IState<string?> renamingSessionId,
    IState<string> renameText,
    IState<string> searchState,
    IChatHistoryService chatService) : ViewBase
{
    public override object Build()
    {
        var newChatBtn = new Button()
            .Icon(Icons.Plus)
            .Ghost()
            .OnClick(() =>
            {
                var newSess = chatService.CreateSession(selectedAgent.Value, selectedModel.Value);
                activeSessionId.Set(newSess.Id);
                sessionVersion.Set(v => v + 1);
            });

        var searchInput = searchState.ToSearchInput()
            .Placeholder("Search history...")
            .Suffix(newChatBtn);

        var sidebarHeader = Layout.Vertical().Height(Size.Px(40)).AlignContent(Align.Center)
            | searchInput;

        var filteredSessions = sessions;
        if (!string.IsNullOrWhiteSpace(searchState.Value))
        {
            var query = searchState.Value.Trim();
            filteredSessions = sessions.Where(s => s.Title.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        object sidebarContent;
        if (filteredSessions.Count == 0 && !string.IsNullOrWhiteSpace(searchState.Value))
        {
            sidebarContent = new NoResultsView();
        }
        else
        {
            sidebarContent = new List(filteredSessions.Select(sess =>
            {
                var isSelected = sess.Id == activeSessionId.Value;
                var formattedDate = sess.UpdatedAt.ToString("M/d, h:mm tt");
                var displayTitle = string.IsNullOrWhiteSpace(sess.Title) ? "New Chat" : sess.Title;

                var textStack = Layout.Vertical().AlignContent(Align.Left).Width(Size.Grow())
                    | Text.Block(displayTitle).Small().NoWrap().Overflow(Overflow.Ellipsis)
                    | Text.Muted($"{formattedDate} • {sess.AgentId}").Small().NoWrap().Overflow(Overflow.Ellipsis);

                var sessionBtn = new Button()
                    .Width(Size.Grow())
                    .Content(textStack)
                    .OnClick(() => activeSessionId.Set(sess.Id))
                    .BorderRadius(BorderRadius.None)
                    .Ghost();

                var actionButtons = Layout.Horizontal().AlignContent(Align.Center).Width(Size.Fit())
                    | new Button()
                        .Icon(Icons.Pencil)
                        .Ghost()
                        .Small()
                        .OnClick(() =>
                        {
                            renamingSessionId.Set(sess.Id);
                            var cleanTitle = sess.Title.TrimEnd('.').TrimEnd('…').Trim();
                            renameText.Set(cleanTitle);
                        })
                    | new Button()
                        .Icon(Icons.Trash2)
                        .Ghost()
                        .Small()
                        .OnClick(() =>
                        {
                            chatService.DeleteSession(sess.Id);
                            sessionVersion.Set(v => v + 1);
                            if (activeSessionId.Value == sess.Id)
                            {
                                var remaining = chatService.GetSessions();
                                activeSessionId.Set(remaining.FirstOrDefault()?.Id);
                            }
                        });

                var rowLayout = Layout.Horizontal().Width(Size.Full()).AlignContent(Align.SpaceBetween)
                    | sessionBtn
                    | actionButtons;

                return isSelected ? rowLayout.Background(Colors.Secondary) : rowLayout;
            }));
        }

        return new HeaderLayout(sidebarHeader, sidebarContent).Scroll(Scroll.None);
    }
}
