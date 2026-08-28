using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Ivy;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Apps.Jobs.Sheets;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Apps.Views.Sheets;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Hooks;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Jobs;
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
    IState<bool> isStreaming,
    IState<string?> streamingSessionId,
    IState<HashSet<string>> runningSessionIds,
    IState<Dictionary<string, string>> liveSessionStreams,
    IRef<ConcurrentQueue<ChatSendMessageDto>> messageQueue,
    IRef<IAgentSession?> activeSessionRef,
    List<ChatSessionDto> sessionDtos,
    List<AgentOptionDto> agentDtos,
    List<ModelOptionDto> modelDtos,
    List<EffortOptionDto> effortDtos,
    bool supportsEffort,
    List<ChatTrackedJobDto> trackedJobs,
    List<ChatTrackedPlanDto> trackedPlans,
    IChatHistoryService chatService,
    IJobService jobService,
    IPlanReaderService planService,
    IConfigService configService,
    IAgentRunner agentRunner,
    Action<ChatSendMessageDto> sendMessage) : ViewBase
{
    public override object Build()
    {
        var openFile = UseState<string?>(null);

        var (planSheet, showPlan) = UseTrigger<string>((isOpen, planPath) =>
        {
            if (!isOpen.Value) return null;
            var planSheetView = new PlanSheet(planPath, planService, openFile, configService);
            var sheet = new Sheet(
                () => isOpen.Set(false),
                planSheetView.Build(),
                planSheetView.GetSheetTitle()
            ).Width(UxHelper.SheetWidth).Resizable();
            return new Fragment(sheet, new FileSheet(openFile, configService));
        });

        var (outputSheet, showOutput) = UseTrigger<string>((isOpen, jobId) =>
        {
            if (!isOpen.Value) return null;
            var job = jobService.GetJob(jobId);
            var title = job is not null ? $"{job.Type} {job.ResolvePlanId()}" : "Job Output";
            return new Sheet(
                () => isOpen.Set(false),
                new OutputSheet(jobId, jobService),
                title
            ).Width(UxHelper.SheetWidth).Resizable();
        });

        var navigateToPlan = Context.UsePlanNavigation(planService, showPlan);

        if (activeSession == null)
        {
            var newChatBtn = new Button("Start New Chat")
                .Icon(Icons.Plus)
                .Primary()
                .OnClick(() =>
                {
                    var newSess = chatService.CreateSession(selectedAgent.Value, selectedModel.Value, effort: selectedEffort.Value);
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

        var widget = new ChatWidget
        {
            ActiveSessionId = activeSessionId.Value,
            StreamingSessionId = streamingSessionId.Value,
            Sessions = sessionDtos,
            Agents = agentDtos,
            Models = modelDtos,
            Efforts = effortDtos,
            TrackedJobs = trackedJobs,
            TrackedPlans = trackedPlans,
            SelectedAgent = selectedAgent.Value,
            SelectedModel = selectedModel.Value,
            SelectedEffort = selectedEffort.Value,
            SupportsEffort = supportsEffort,
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
                var newSess = chatService.CreateSession(selectedAgent.Value, selectedModel.Value, effort: selectedEffort.Value);
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
            OnNavigatePlan = e =>
            {
                var target = e.Value;
                if (!string.IsNullOrEmpty(target))
                {
                    if (int.TryParse(target.Length >= 5 ? target[..5] : target, out var planId))
                    {
                        navigateToPlan(planId);
                    }
                    else
                    {
                        var plan = planService.GetPlanByFolder(target);
                        if (plan != null)
                        {
                            navigateToPlan(plan.Id);
                        }
                        else
                        {
                            showPlan(target);
                        }
                    }
                }
                return ValueTask.CompletedTask;
            },
            OnNavigateJob = e =>
            {
                if (!string.IsNullOrEmpty(e.Value))
                {
                    showOutput(e.Value);
                }
                return ValueTask.CompletedTask;
            }
        }
        .WithLayout()
        .Full()
        .RemoveParentPadding();

        return new Fragment(widget, planSheet, outputSheet);
    }
}
