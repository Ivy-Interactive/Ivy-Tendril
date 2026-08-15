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
            var generatingSessionIds = chatService.GetGeneratingSessionIds();

            sidebarContent = new List(filteredSessions.Select(sess =>
            {
                var isSelected = sess.Id == activeSessionId.Value;
                var formattedDate = sess.UpdatedAt.ToString("M/d, h:mm tt");
                var displayTitle = string.IsNullOrWhiteSpace(sess.Title) ? "New Chat" : sess.Title;
                var isGenerating = generatingSessionIds.Contains(sess.Id);
                var lastMessage = sess.Messages.LastOrDefault();
                var isWaiting = !isGenerating && lastMessage != null && lastMessage.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase);

                object metaLine;
                if (isGenerating)
                {
                    metaLine = Layout.Horizontal().AlignContent(Align.Left)
                        | new Icon(Icons.LoaderCircle, Colors.Green).Small()
                        | Text.Success("Generating").Small()
                        | Text.Muted($"• {sess.AgentId}").Small();
                }
                else if (isWaiting)
                {
                    metaLine = Layout.Horizontal().AlignContent(Align.Left)
                        | new Icon(Icons.Clock, Colors.Amber).Small()
                        | Text.Warning("Waiting for input").Small()
                        | Text.Muted($"• {sess.AgentId}").Small();
                }
                else
                {
                    metaLine = Text.Muted($"{formattedDate} • {sess.AgentId}").Small().NoWrap().Overflow(Overflow.Ellipsis);
                }

                object titleBlock = isGenerating
                    ? (Layout.Horizontal().AlignContent(Align.Left)
                        | new Icon(Icons.CircleDot, Colors.Green).Small()
                        | Text.Block(displayTitle).Small().NoWrap().Overflow(Overflow.Ellipsis))
                    : isWaiting
                        ? (Layout.Horizontal().AlignContent(Align.Left)
                            | new Icon(Icons.CircleDot, Colors.Amber).Small()
                            | Text.Block(displayTitle).Small().NoWrap().Overflow(Overflow.Ellipsis))
                        : Text.Block(displayTitle).Small().NoWrap().Overflow(Overflow.Ellipsis);

                var textStack = Layout.Vertical().AlignContent(Align.Left).Width(Size.Full())
                    | titleBlock
                    | metaLine;

                var sessionBtn = new Button()
                    .Width(Size.Full())
                    .Content(textStack)
                    .OnClick(() => activeSessionId.Set(sess.Id))
                    .BorderRadius(BorderRadius.None);

                return isSelected ? sessionBtn.Secondary() : sessionBtn.Ghost();
            }));
        }

        return new HeaderLayout(sidebarHeader, sidebarContent).Scroll(Scroll.None);
    }
}
