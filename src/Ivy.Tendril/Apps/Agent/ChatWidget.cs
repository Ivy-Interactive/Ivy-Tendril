using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ivy;
using Ivy.Core;
using Ivy.Core.Hooks;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Apps.Agent;

public class ChatWidget : ViewBase
{
    private readonly string? _initialPrompt;
    private readonly string? _initialSessionId;

    public ChatWidget(string? initialPrompt = null, string? initialSessionId = null)
    {
        _initialPrompt = initialPrompt;
        _initialSessionId = initialSessionId;
    }

    public override object Build()
    {
        // 1. ALL Ivy Hooks declared at the very top of Build() (IVYHOOK005 compliance)
        var configService = UseService<IConfigService>();
        var chatService = UseService<IChatHistoryService>();
        var agentRunner = UseService<IAgentRunner>();

        var activeSessionId = UseState<string?>(_initialSessionId);
        var searchTerm = UseState("");
        var promptText = UseState("");
        var selectedAgent = UseState(() => configService.Settings.CodingAgent ?? "claude");
        var selectedModel = UseState("opus");
        var isStreaming = UseState(false);
        var streamingText = UseState("");
        var initialHandled = UseRef(false);

        // Fetch history sessions
        var sessions = chatService.GetSessions();
        if (activeSessionId.Value == null && sessions.Count > 0 && !initialHandled.Value && string.IsNullOrEmpty(_initialPrompt))
        {
            activeSessionId.Set(sessions[0].Id);
        }

        var activeSession = activeSessionId.Value != null ? chatService.GetSession(activeSessionId.Value) : null;

        if (activeSession != null)
        {
            if (selectedAgent.Value != activeSession.AgentId && !string.IsNullOrEmpty(activeSession.AgentId))
            {
                selectedAgent.Set(activeSession.AgentId);
            }
            if (selectedModel.Value != activeSession.ModelId && !string.IsNullOrEmpty(activeSession.ModelId))
            {
                selectedModel.Set(activeSession.ModelId);
            }
        }

        // Get dynamic list of registered agents
        var registeredAgentIds = agentRunner.RegisteredAgents;
        if (registeredAgentIds.Count == 0)
        {
            registeredAgentIds = ["claude", "opencode", "codex", "gemini", "antigravity", "copilot", "ivy"];
        }

        var agentOptions = registeredAgentIds.Select(id =>
        {
            var (label, _) = AgentBranding.For(id, agentRunner);
            return (Value: id, Label: label);
        }).ToList();

        // Get dynamic models for current selected agent
        var currentModelOptions = GetModelsForAgent(agentRunner, selectedAgent.Value);

        // Ensure selected model belongs to available options
        if (!currentModelOptions.Any(m => m.Id.Equals(selectedModel.Value, StringComparison.OrdinalIgnoreCase)))
        {
            if (currentModelOptions.Count > 0)
            {
                selectedModel.Set(currentModelOptions[0].Id);
            }
        }

        // Action: Create New Session
        void CreateNewSession()
        {
            var newSess = chatService.CreateSession(selectedAgent.Value, selectedModel.Value);
            activeSessionId.Set(newSess.Id);
            promptText.Set("");
        }

        // Action: Submit Message
        async Task SendMessage(string userPrompt)
        {
            var trimmed = userPrompt.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || isStreaming.Value) return;

            string targetSessionId = activeSessionId.Value ?? "";
            if (string.IsNullOrEmpty(targetSessionId))
            {
                var newSess = chatService.CreateSession(selectedAgent.Value, selectedModel.Value);
                targetSessionId = newSess.Id;
                activeSessionId.Set(targetSessionId);
            }

            chatService.AddMessage(targetSessionId, "user", trimmed, selectedAgent.Value, selectedModel.Value);
            promptText.Set("");
            isStreaming.Set(true);
            streamingText.Set("Thinking...");

            try
            {
                var context = new AgentResolutionContext
                {
                    AgentId = selectedAgent.Value,
                    Prompt = trimmed,
                    ModelOverride = selectedModel.Value,
                    WorkingDirectory = !string.IsNullOrEmpty(configService.TendrilHome) ? configService.TendrilHome : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    PermissionMode = PermissionMode.FullAuto,
                };

                var session = await agentRunner.LaunchAsync(context);
                var result = await session.WaitForCompletionAsync();

                var responseContent = !string.IsNullOrWhiteSpace(result.Response)
                    ? result.Response
                    : (result.IsSuccess ? "Task completed successfully." : "Agent execution completed with status code " + (result.ExitCode?.ToString() ?? "unknown"));

                chatService.AddMessage(targetSessionId, "assistant", responseContent, selectedAgent.Value, selectedModel.Value);
            }
            catch (Exception ex)
            {
                chatService.AddMessage(targetSessionId, "assistant", $"Error executing request: {ex.Message}", selectedAgent.Value, selectedModel.Value);
            }
            finally
            {
                isStreaming.Set(false);
                streamingText.Set("");
            }
        }

        // Auto execute initial prompt if provided
        if (!initialHandled.Value && !string.IsNullOrWhiteSpace(_initialPrompt))
        {
            initialHandled.Value = true;
            _ = Task.Run(async () => await SendMessage(_initialPrompt));
        }

        // Filter sessions by search term
        var filteredSessions = sessions.Where(s =>
            string.IsNullOrWhiteSpace(searchTerm.Value) ||
            s.Title.Contains(searchTerm.Value, StringComparison.OrdinalIgnoreCase) ||
            s.Messages.Any(m => m.Content.Contains(searchTerm.Value, StringComparison.OrdinalIgnoreCase))
        ).ToList();

        // Build Left History Sidebar
        var newChatBtn = new Button("New Chat")
            .Icon(Icons.Plus)
            .Small()
            .Width(Size.Full())
            .OnClick(CreateNewSession);

        var searchBox = searchTerm.ToSearchInput().Placeholder("Search history...").Width(Size.Full());

        var sidebarHeader = Layout.Vertical().Gap(1).Padding(2)
            | Layout.Horizontal().AlignContent(Align.SpaceBetween).Width(Size.Full())
                | Text.H3("Chat History")
            | newChatBtn
            | searchBox;

        var historyListItems = new List<object>();
        foreach (var sess in filteredSessions)
        {
            var (agentLabel, _) = AgentBranding.For(sess.AgentId, agentRunner);

            var deleteBtn = new Button()
                .Icon(Icons.Trash2)
                .Ghost()
                .Small()
                .Tooltip("Delete chat")
                .OnClick(() =>
                {
                    chatService.DeleteSession(sess.Id);
                    if (activeSessionId.Value == sess.Id)
                    {
                        var remaining = chatService.GetSessions();
                        activeSessionId.Set(remaining.FirstOrDefault()?.Id);
                    }
                });

            var sessionItem = new Card(
                Layout.Horizontal().AlignContent(Align.SpaceBetween).Width(Size.Full()).Padding(2)
                | Layout.Vertical().Gap(0).Width(Size.Grow())
                    | Text.Block(sess.Title).NoWrap().Overflow(Overflow.Ellipsis)
                    | Layout.Horizontal().Gap(1)
                        | Text.Muted(sess.UpdatedAt.ToString("g"))
                        | Text.Muted($"• {agentLabel}")
                | deleteBtn
            ).OnClick(() => activeSessionId.Set(sess.Id));

            historyListItems.Add(sessionItem);
        }

        var sidebarList = Layout.Vertical().Gap(1).Padding(2)
            | (historyListItems.Count > 0 ? historyListItems.ToArray() : [Text.Muted("No chat history found.")]);

        var leftSidebar = Layout.Vertical().Width(Size.Px(300)).Height(Size.Full())
            | sidebarHeader
            | sidebarList;

        // Build Right Main Chat Pane
        object mainPane;
        if (activeSession == null && !isStreaming.Value)
        {
            mainPane = Layout.Vertical().AlignContent(Align.Center).Height(Size.Full()).Gap(2)
                | Text.H2("Start a conversation")
                | Text.Muted("Choose an agent and model below to begin chatting.")
                | new Button("Create New Chat").Icon(Icons.Plus).OnClick(CreateNewSession);
        }
        else
        {
            var currentAgentId = activeSession?.AgentId ?? selectedAgent.Value;
            var currentModelId = activeSession?.ModelId ?? selectedModel.Value;
            var (activeAgentLabel, _) = AgentBranding.For(currentAgentId, agentRunner);

            // Header Bar
            var chatHeader = Layout.Horizontal().AlignContent(Align.SpaceBetween).Padding(2).Width(Size.Full())
                | Layout.Horizontal().Gap(2).AlignContent(Align.Left)
                    | Text.H2(activeSession?.Title ?? "New Chat")
                    | Layout.Horizontal().Gap(1)
                        | new Badge(activeAgentLabel)
                        | new Badge(currentModelId)
                | Layout.Horizontal();

            // Messages List Container
            var messageCards = new List<object>();
            if (activeSession?.Messages != null)
            {
                foreach (var msg in activeSession.Messages)
                {
                    if (msg.Role == "user")
                    {
                        messageCards.Add(
                            Layout.Horizontal().AlignContent(Align.Right).Width(Size.Full()).Padding(1, 0)
                            | new Card(
                                Layout.Vertical().Gap(1).Padding(2)
                                | Text.Block(msg.Content)
                                | Text.Muted(msg.Timestamp.ToString("t"))
                            ).Width(Size.Percent(70))
                        );
                    }
                    else
                    {
                        var (msgAgentLabel, _) = AgentBranding.For(msg.AgentId ?? currentAgentId, agentRunner);
                        messageCards.Add(
                            Layout.Horizontal().AlignContent(Align.Left).Width(Size.Full()).Padding(1, 0)
                            | new Card(
                                Layout.Vertical().Gap(1).Padding(2)
                                | Layout.Horizontal().Gap(1)
                                    | Text.H4(msgAgentLabel)
                                    | Text.Muted(msg.ModelId ?? currentModelId)
                                | new DraftMarkdown(msg.Content)
                                | Text.Muted(msg.Timestamp.ToString("t"))
                            ).Width(Size.Percent(85))
                        );
                    }
                }
            }

            if (isStreaming.Value)
            {
                messageCards.Add(
                    Layout.Horizontal().AlignContent(Align.Left).Width(Size.Full()).Padding(1, 0)
                    | new Card(
                        Layout.Vertical().Gap(1).Padding(2)
                        | Text.H4(activeAgentLabel)
                        | Text.Muted(streamingText.Value)
                    ).Width(Size.Percent(85))
                );
            }

            var messagesView = Layout.Vertical().Gap(2).Padding(2).Width(Size.Full())
                | (messageCards.Count > 0 ? messageCards.ToArray() : [Text.Muted("No messages yet. Send a prompt below.")]);

            // Toolbar Selectors (Agentic CLI Picker & Dynamic Model Picker)
            var agentSelectOptions = agentOptions.Select(a => new BadgeSelectOption(a.Value, a.Label)).ToArray();
            var agentPicker = new BadgeSelect
            {
                Options = agentSelectOptions,
                Value = [selectedAgent.Value],
                Placeholder = "Select Agent",
                Icon = "Bot",
                Multiple = false,
                Tooltip = "Agentic CLI",
            }.WithOnChange(vals =>
            {
                var val = vals.FirstOrDefault();
                if (!string.IsNullOrEmpty(val))
                {
                    selectedAgent.Set(val);
                    var newModels = GetModelsForAgent(agentRunner, val);
                    if (newModels.Count > 0)
                    {
                        selectedModel.Set(newModels[0].Id);
                    }
                }
            });

            var modelSelectOptions = currentModelOptions.Select(m => new BadgeSelectOption(m.Id, m.DisplayName)).ToArray();
            var modelPicker = new BadgeSelect
            {
                Options = modelSelectOptions,
                Value = [selectedModel.Value],
                Placeholder = "Select Model",
                Icon = "Cpu",
                Multiple = false,
                Tooltip = "Model",
            }.WithOnChange(vals =>
            {
                var val = vals.FirstOrDefault();
                if (!string.IsNullOrEmpty(val))
                {
                    selectedModel.Set(val);
                }
            });

            // Footer Input Controls
            var chatInputWidget = new Ivy.Tendril.Widgets.ContentInput
            {
                Value = promptText.Value,
                SubmitLabel = "Send",
                TranscriptionUrl = "wss://tendril-api.ivy.app/transcribe/ws",
                Placeholder = $"Ask {activeAgentLabel}...",
                OnChange = e =>
                {
                    promptText.Set(e.Value);
                    return ValueTask.CompletedTask;
                },
                OnSubmit = e =>
                {
                    _ = SendMessage(e.Value.Value);
                    return ValueTask.CompletedTask;
                }
            };

            var inputToolbar = Layout.Vertical().Gap(1).Padding(2).Width(Size.Full())
                | Layout.Horizontal().Gap(2).AlignContent(Align.Left).Width(Size.Full())
                    | agentPicker
                    | modelPicker
                | chatInputWidget;

            mainPane = Layout.Vertical().Width(Size.Grow()).Height(Size.Full())
                | chatHeader
                | messagesView
                | inputToolbar;
        }

        return Layout.Horizontal().Width(Size.Full()).Height(Size.Full())
            | leftSidebar
            | mainPane;
    }

    private static List<(string Id, string DisplayName)> GetModelsForAgent(IAgentRunner runner, string agentId)
    {
        var normalized = AgentProviderFactory.NormalizeAgentName(agentId);
        var catalog = runner.GetModelCatalog(normalized);
        if (catalog != null)
        {
            var staticModels = catalog.GetStaticModels();
            if (staticModels != null && staticModels.Count > 0)
            {
                return staticModels.Select(m => (m.Id, m.DisplayName)).ToList();
            }
        }

        return normalized switch
        {
            AgentId.Claude => [
                ("opus", "Claude Opus"),
                ("claude-opus-5", "Claude Opus 5"),
                ("sonnet", "Claude Sonnet"),
                ("haiku", "Claude Haiku")
            ],
            AgentId.OpenCode => [
                ("kimi-k3", "Kimi k3"),
                ("deepseek-v3", "DeepSeek V3"),
                ("default", "OpenCode Default")
            ],
            AgentId.Gemini => [
                ("gemini-2.5-pro", "Gemini 2.5 Pro"),
                ("gemini-2.5-flash", "Gemini 2.5 Flash")
            ],
            AgentId.Codex => [
                ("gpt-4o", "GPT-4o"),
                ("o3-mini", "o3-mini"),
                ("o1", "o1")
            ],
            AgentId.Antigravity => [
                ("default", "Antigravity Default")
            ],
            AgentId.Copilot => [
                ("default", "Copilot Default")
            ],
            _ => [("default", "Default")]
        };
    }
}
