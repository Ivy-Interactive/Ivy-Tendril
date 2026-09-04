using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Ivy;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Apps.Chat.Dialogs;
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
    IState<string> selectedEffort,
    List<ChatSessionDto> sessionDtos,
    List<AgentOptionDto> agentDtos,
    List<ModelOptionDto> modelDtos,
    List<EffortOptionDto> effortDtos,
    bool supportsEffort,
    bool isStreaming,
    string streamingText,
    IChatHistoryService chatService,
    IChatExecutionService executionService,
    IAgentRunner agentRunner,
    Action<ChatSendMessageDto> sendMessage,
    Action<string> selectSession) : ViewBase
{
    public override object Build()
    {
        var configService = UseService<IConfigService>();
        var deletingSessionId = UseState<string?>(null);

        var upload = UseUpload(async (fileUpload, stream, ct) =>
        {
            var targetSession = activeSessionId.Value ?? "temp";
            var attachDir = Path.Combine(configService.TendrilHome, "Attachments", targetSession);
            Directory.CreateDirectory(attachDir);
            var rawName = Path.GetFileName(fileUpload.FileName);
            var safeFileName = !string.IsNullOrWhiteSpace(rawName)
                ? string.Concat(rawName.Split(Path.GetInvalidFileNameChars()))
                : $"file_{Guid.NewGuid():N}.bin";
            if (string.IsNullOrWhiteSpace(safeFileName)) safeFileName = $"file_{Guid.NewGuid():N}.bin";
            var filePath = Path.Combine(attachDir, safeFileName);
            await using var fileStream = File.Create(filePath);
            await stream.CopyToAsync(fileStream, ct);
        });

        if (activeSession == null)
        {
            var newChatBtn = new Button("Start New Chat")
                .Icon(Icons.Plus)
                .Primary()
                .OnClick(() =>
                {
                    var newSess = chatService.CreateSession(selectedAgent.Value, selectedModel.Value, effort: selectedEffort.Value);
                    selectSession(newSess.Id);
                });

            return Layout.Vertical().AlignContent(Align.Center).Width(Size.Full()).Height(Size.Full())
                | Icons.MessageSquare.ToIcon().Size(Size.Px(48)).Color(Colors.Muted)
                | Text.H3("No Chat Selected")
                | Text.Muted("Select an existing chat session from history or start a new chat.")
                | newChatBtn;
        }

        var sessionToDelete = deletingSessionId.Value != null
            ? chatService.GetSession(deletingSessionId.Value) ?? activeSession
            : activeSession;

        var deleteDialog = new DeleteSessionDialog(deletingSessionId, sessionToDelete, chatService, activeSessionId, sessionVersion);

        var activeQueuedItems = activeSessionId.Value != null
            ? chatService.GetQueuedMessages(activeSessionId.Value)
            : Array.Empty<ChatQueuedItem>();

        var queuedMessageDtos = activeQueuedItems.Select(q => new ChatQueuedMessageDto(
            q.Id,
            q.Prompt,
            q.Attachments
        )).ToList();

        var chatWidget = new ChatWidget
        {
            ActiveSessionId = activeSessionId.Value,
            UploadUrl = upload.Value.UploadUrl,
            Sessions = sessionDtos,
            Agents = agentDtos,
            Models = modelDtos,
            Efforts = effortDtos,
            SelectedAgent = selectedAgent.Value,
            SelectedModel = selectedModel.Value,
            SelectedEffort = selectedEffort.Value,
            SupportsEffort = supportsEffort,
            IsStreaming = isStreaming,
            StreamingText = streamingText,
            QueuedMessages = queuedMessageDtos,

            OnSelectSession = e =>
            {
                if (!string.IsNullOrEmpty(e.Value))
                {
                    selectSession(e.Value);
                }
                return ValueTask.CompletedTask;
            },
            OnDeleteSession = e =>
            {
                deletingSessionId.Set(e.Value);
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
                var newSess = chatService.CreateSession(selectedAgent.Value, selectedModel.Value, effort: selectedEffort.Value);
                selectSession(newSess.Id);
                return ValueTask.CompletedTask;
            },
            OnSendMessage = e =>
            {
                sendMessage(e.Value);
                return ValueTask.CompletedTask;
            },
            OnCancelStream = async _ =>
            {
                if (activeSessionId.Value != null)
                {
                    chatService.ClearQueuedMessages(activeSessionId.Value);
                    await executionService.CancelAsync(activeSessionId.Value);
                }
            },
            OnAgentChanged = e =>
            {
                selectedAgent.Set(e.Value);
                var newModels = ChatApp.GetModelsForAgent(agentRunner, e.Value);
                if (newModels.Count > 0)
                {
                    selectedModel.Set(newModels[0].Id);
                }
                selectedEffort.Set("default");
                return ValueTask.CompletedTask;
            },
            OnModelChanged = e =>
            {
                selectedModel.Set(e.Value);
                selectedEffort.Set("default");
                return ValueTask.CompletedTask;
            },
            OnEffortChanged = e =>
            {
                selectedEffort.Set(e.Value);
                return ValueTask.CompletedTask;
            },
            OnDeleteQueuedMessage = e =>
            {
                if (activeSessionId.Value != null && !string.IsNullOrEmpty(e.Value))
                {
                    chatService.RemoveQueuedMessage(activeSessionId.Value, e.Value);
                }
                return ValueTask.CompletedTask;
            },
            OnUpdateQueuedMessage = e =>
            {
                if (activeSessionId.Value != null && e.Value != null && e.Value.Length >= 2)
                {
                    chatService.UpdateQueuedMessage(activeSessionId.Value, e.Value[0], e.Value[1]);
                }
                return ValueTask.CompletedTask;
            },
            OnSendQueuedNow = e =>
            {
                if (activeSessionId.Value != null && !string.IsNullOrEmpty(e.Value))
                {
                    var items = chatService.GetQueuedMessages(activeSessionId.Value);
                    var item = items.FirstOrDefault(q => q.Id == e.Value);
                    if (item != null)
                    {
                        chatService.RemoveQueuedMessage(activeSessionId.Value, e.Value);
                        sendMessage(new ChatSendMessageDto(item.Prompt, item.Attachments, activeSessionId.Value));
                    }
                }
                return ValueTask.CompletedTask;
            }
        }
        .WithLayout()
        .Full()
        .RemoveParentPadding();

        return new Fragment(chatWidget, deleteDialog);
    }
}
