using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ivy;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Apps.Chat;

public class ContentView(
    ChatSessionModel? activeSession,
    IState<string?> activeSessionId,
    IState<int> sessionVersion,
    IState<string> selectedAgent,
    IState<string> selectedModel,
    IState<bool> isStreaming,
    IState<string?> streamingSessionId,
    IState<HashSet<string>> runningSessionIds,
    IState<Dictionary<string, string>> liveSessionStreams,
    IRef<ConcurrentQueue<ChatSendMessageDto>> messageQueue,
    IRef<IAgentSession?> activeSessionRef,
    List<ChatSessionDto> sessionDtos,
    List<AgentOptionDto> agentDtos,
    List<ModelOptionDto> modelDtos,
    IChatHistoryService chatService,
    IAgentRunner agentRunner,
    Action<ChatSendMessageDto> sendMessage) : ViewBase
{
    public override object Build()
    {
        if (activeSession == null)
        {
            var newChatBtn = new Button("Start New Chat")
                .Icon(Icons.Plus)
                .Primary()
                .OnClick(() =>
                {
                    var newSess = chatService.CreateSession(selectedAgent.Value, selectedModel.Value);
                    activeSessionId.Set(newSess.Id);
                    sessionVersion.Set(v => v + 1);
                });

            return Layout.Vertical().AlignContent(Align.Center).Width(Size.Full()).Height(Size.Full())
                | Icons.MessageSquare.ToIcon().Size(Size.Px(48)).Color(Colors.Muted)
                | Text.H3("No Chat Selected")
                | Text.Muted("Select an existing chat session from history or start a new chat.")
                | newChatBtn;
        }

        string activeSessionLiveStream = activeSessionId.Value != null && liveSessionStreams.Value.TryGetValue(activeSessionId.Value, out var streamText)
            ? streamText
            : "";

        return new ChatWidget
        {
            ActiveSessionId = activeSessionId.Value,
            StreamingSessionId = streamingSessionId.Value,
            Sessions = sessionDtos,
            Agents = agentDtos,
            Models = modelDtos,
            SelectedAgent = selectedAgent.Value,
            SelectedModel = selectedModel.Value,
            IsStreaming = activeSessionId.Value != null && runningSessionIds.Value.Contains(activeSessionId.Value),
            StreamingText = activeSessionLiveStream,

            OnSelectSession = e =>
            {
                activeSessionId.Set(e.Value);
                return ValueTask.CompletedTask;
            },
            OnDeleteSession = e =>
            {
                chatService.DeleteSession(e.Value);
                sessionVersion.Set(v => v + 1);
                if (activeSessionId.Value == e.Value)
                {
                    var remaining = chatService.GetSessions();
                    activeSessionId.Set(remaining.FirstOrDefault()?.Id);
                }
                return ValueTask.CompletedTask;
            },
            OnRenameSession = e =>
            {
                if (e.Value != null && e.Value.Length >= 2)
                {
                    chatService.RenameSession(e.Value[0], e.Value[1]);
                    sessionVersion.Set(v => v + 1);
                }
                return ValueTask.CompletedTask;
            },
            OnCreateSession = _ =>
            {
                var newSess = chatService.CreateSession(selectedAgent.Value, selectedModel.Value);
                activeSessionId.Set(newSess.Id);
                return ValueTask.CompletedTask;
            },
            OnSendMessage = e =>
            {
                sendMessage(e.Value);
                return ValueTask.CompletedTask;
            },
            OnCancelStream = async _ =>
            {
                while (messageQueue.Value.TryDequeue(out var _)) { }
                try
                {
                    if (activeSessionRef.Value != null)
                    {
                        await activeSessionRef.Value.StopAsync();
                    }
                }
                catch
                {
                    // Ignore cancel exceptions
                }
                isStreaming.Set(false);
                streamingSessionId.Set(null);
                runningSessionIds.Set(new HashSet<string>());
                liveSessionStreams.Set(new Dictionary<string, string>());
            },
            OnAgentChanged = e =>
            {
                selectedAgent.Set(e.Value);
                var newModels = ChatApp.GetModelsForAgent(agentRunner, e.Value);
                if (newModels.Count > 0)
                {
                    selectedModel.Set(newModels[0].Id);
                }
                return ValueTask.CompletedTask;
            },
            OnModelChanged = e =>
            {
                selectedModel.Set(e.Value);
                return ValueTask.CompletedTask;
            }
        }
        .WithLayout()
        .Full()
        .RemoveParentPadding();
    }
}
