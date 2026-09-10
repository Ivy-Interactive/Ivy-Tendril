using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Ivy;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.AppShell.Dialogs;
using Ivy.Tendril.Apps.Chat.Dialogs;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Plans;
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
    string greeting,
    string headline,
    IChatHistoryService chatService,
    IChatExecutionService executionService,
    IAgentRunner agentRunner,
    Action<ChatSendMessageDto> sendMessage,
    Action<string> selectSession,
    Action startNewChat,
    bool embedded = false) : ViewBase
{
    internal IState<string> SelectedAgentState => selectedAgent;
    internal IState<string> SelectedModelState => selectedModel;
    internal IState<string> SelectedEffortState => selectedEffort;

    /// <summary>The plan a job event names, by folder, numeric id or zero-padded id.</summary>
    internal static PlanFile? FindPlan(IPlanReaderService planService, string planId)
    {
        var trimmed = planId.TrimStart('0');
        return planService.GetPlans().FirstOrDefault(p =>
            p.FolderName.Equals(planId, StringComparison.OrdinalIgnoreCase) ||
            (trimmed.Length > 0 && p.Id.ToString() == trimmed) ||
            p.FolderName.StartsWith(planId + "-", StringComparison.OrdinalIgnoreCase));
    }

    public override object Build()
    {
        var configService = UseService<IConfigService>();
        Context.TryUseService<IJobService>(out var jobService);
        Context.TryUseService<IPlanReaderService>(out var planService);
        var navigator = UseNavigation();
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

        _ = sessionVersion.Value;

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

        var runningJobs = (activeSessionId.Value != null && jobService != null)
            ? jobService.GetJobs()
                .Where(j => string.Equals(j.ChatSessionId, activeSessionId.Value, StringComparison.OrdinalIgnoreCase)
                         && (j.Status == JobStatus.Running || j.Status == JobStatus.Pending || j.Status == JobStatus.Queued))
                .Select(ChatApp.ToJobDto).ToList()
            : new List<ChatJobDto>();

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
            RunningJobs = runningJobs,
            Greeting = greeting,
            Headline = headline,
            Embedded = embedded,

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
                startNewChat();
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
                string? initialModel = null;
                if (newModels.Count > 0)
                {
                    initialModel = newModels[0].Id;
                    selectedModel.Set(initialModel);
                }
                selectedEffort.Set("default");
                configService.Settings.LastChatAgent = e.Value;
                if (initialModel != null)
                {
                    configService.Settings.LastChatModel = initialModel;
                }
                configService.SaveSettings();
                return ValueTask.CompletedTask;
            },
            OnModelChanged = e =>
            {
                selectedModel.Set(e.Value);
                selectedEffort.Set("default");
                configService.Settings.LastChatModel = e.Value;
                configService.Settings.LastChatAgent = selectedAgent.Value;
                configService.SaveSettings();
                return ValueTask.CompletedTask;
            },
            OnEffortChanged = e =>
            {
                selectedEffort.Set(e.Value);
                configService.Settings.LastChatEffort = e.Value;
                configService.SaveSettings();
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
                        sendMessage(new ChatSendMessageDto(item.Prompt, item.Attachments, activeSessionId.Value, ForceSend: true));
                    }
                }
                return ValueTask.CompletedTask;
            },
            OnAnswerQuestion = e =>
            {
                if (e.Value != null)
                {
                    chatService.ApplyQuestionAnswers(e.Value.SessionId, e.Value.MessageId, e.Value.Answers);
                    sessionVersion.Set(v => v + 1);
                    if (!string.IsNullOrWhiteSpace(e.Value.ResponseText))
                    {
                        sendMessage(new ChatSendMessageDto(e.Value.ResponseText, SessionId: e.Value.SessionId));
                    }
                }
                return ValueTask.CompletedTask;
            },
            OnOpenPlan = e =>
            {
                if (planService == null || string.IsNullOrEmpty(e.Value)) return ValueTask.CompletedTask;
                var plan = FindPlan(planService, e.Value);
                if (plan != null)
                {
                    var (app, appArgs) = PlanSearchDialog.ResolveTarget(plan);
                    navigator.Navigate(app, appArgs);
                }
                return ValueTask.CompletedTask;
            }
        }
        .WithLayout()
        .Full();

        // The panel that hosts an embedded chat draws its own inset; RemoveParentPadding would
        // zero it (the framework strips padding from every ancestor wrapper of that class).
        if (!embedded)
            chatWidget = chatWidget.RemoveParentPadding();

        return new Fragment(chatWidget, deleteDialog);
    }
}
