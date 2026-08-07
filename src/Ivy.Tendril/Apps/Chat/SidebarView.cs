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

                var textStack = Layout.Vertical().Gap(1).AlignContent(Align.Left)
                    | Text.Literal(displayTitle).Small()
                    | Text.Muted($"{formattedDate} • {sess.AgentId}").Small();

                var actionButtons = Layout.Horizontal().Gap(1).AlignContent(Align.Center)
                    | new Button()
                        .Icon(Icons.Pencil)
                        .Ghost()
                        .Small()
                        .OnClick(() =>
                        {
                            renamingSessionId.Set(sess.Id);
                            renameText.Set(sess.Title);
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
                    | textStack
                    | actionButtons;

                var rowBtn = new Button()
                    .Width(Size.Full())
                    .Content(rowLayout)
                    .OnClick(() => activeSessionId.Set(sess.Id))
                    .BorderRadius(BorderRadius.None);

                return isSelected ? rowBtn.Secondary() : rowBtn.Ghost();
            }));
        }

        return new HeaderLayout(sidebarHeader, sidebarContent);
    }
}
