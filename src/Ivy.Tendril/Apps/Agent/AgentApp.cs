using System;
using System.Diagnostics;
using System.Linq;
using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Ivy.Hooks.Pty;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Jobs;
using Ivy.Tendril.Services.Promptware;
using Ivy.Tendril.Models;
using Ivy.Tendril.Apps.Settings;
using Ivy.Widgets.Xterm;
using Xterm = Ivy.Widgets.Xterm;
using System.ComponentModel;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Apps.Agent;

[App(title: "Agent App", icon: Icons.Terminal, group: ["Orchestration"], order: Constants.Agent, isVisible: true, allowDuplicateTabs: false)]
public class AgentApp : ViewBase
{
    public record ChatSession(string Id, string Title, string Type, string? AgentName = null);

    public enum ChatType
    {
        CLI,
        Agent
    }

    public override object Build()
    {
        var configService = UseService<IConfigService>();
        var agentRunner = UseService<IAgentRunner>();
        var sessions = UseState(ImmutableArray.Create<ChatSession>(
            new ChatSession("session-1", $"{agentRunner.GetCli(configService.Settings.CodingAgent).DisplayName} CLI 1", "CLI", configService.Settings.CodingAgent)
        ));
        var selectedIndex = UseState<int?>(0);

        var chosenType = UseState(ChatType.CLI);
        var chosenAgent = UseState(configService.Settings.CodingAgent ?? "");
        var makeDefault = UseState(false);
        var refreshTrigger = UseState(0);

        var (dialogView, triggerAddDialog) = UseTrigger((IState<bool> isOpen) =>
        {
            var typeOptions = new[]
            {
                new Option<ChatType>("CLI-chat (PTY Terminal)", ChatType.CLI),
                new Option<ChatType>("Agent chat (Conversational UI)", ChatType.Agent)
            };

            var agentOptions = agentRunner.RegisteredAgents
                .Select(id => new Option<string>(agentRunner.GetCli(id).DisplayName, id))
                .ToArray();

            var dialogContent = Layout.Vertical(
                Text.P("Choose the type of chat session you want to create:"),
                chosenType.ToSelectInput(typeOptions).Radio(),
                Layout.Vertical(
                    Text.P("Choose which Agent to chat with:"),
                    chosenAgent.ToSelectInput(agentOptions)
                ),
                makeDefault.ToBoolInput()
                    .Label("Make this the default option in the future (skip this dialog)")
            );

            return new Dialog(
                _ => isOpen.Set(false),
                new DialogHeader("Add New Chat Session"),
                new DialogBody(dialogContent),
                new DialogFooter(
                    new Button("Cancel").Outline().OnClick(() => isOpen.Set(false)),
                    new Button("Create").Primary().ShortcutKey("Enter").AutoFocus().OnClick(() =>
                    {
                        HandleCreateChatFromDialog();
                        isOpen.Set(false);
                    })
                )
            );
        });

        void HandleClose(int index)
        {
            var list = sessions.Value.RemoveAt(index);
            sessions.Set(list);

            if (list.Length == 0)
            {
                selectedIndex.Set(null);
                return;
            }

            var nextIndex = selectedIndex.Value ?? 0;
            if (nextIndex >= list.Length)
                nextIndex = list.Length - 1;
            if (nextIndex < 0)
                nextIndex = 0;
            selectedIndex.Set(nextIndex);
        }

        void HandleAddTabClick()
        {
            if (configService.Settings.AlwaysUseDefaultChatType)
            {
                var nextId = Guid.NewGuid().ToString();
                var type = configService.Settings.DefaultChatType ?? "CLI";
                var agentName = configService.Settings.CodingAgent;
                var count = sessions.Value.Count(s => s.Type == type && s.AgentName == agentName) + 1;
                var displayName = agentRunner.GetCli(agentName).DisplayName;
                var title = type == "CLI"
                    ? $"{displayName} CLI {count}"
                    : $"{displayName} {count}";
                var newSession = new ChatSession(nextId, title, type, agentName);
                var updated = sessions.Value.Add(newSession);
                sessions.Set(updated);
                selectedIndex.Set(updated.Length - 1);
            }
            else
            {
                triggerAddDialog();
            }
        }

        void HandleCreateChatFromDialog()
        {
            var type = chosenType.Value.ToString();
            var agentName = chosenAgent.Value;
            var count = sessions.Value.Count(s => s.Type == type && s.AgentName == agentName) + 1;
            var displayName = agentRunner.GetCli(agentName).DisplayName;
            var title = type == "CLI"
                ? $"{displayName} CLI {count}"
                : $"{displayName} {count}";
            var nextId = Guid.NewGuid().ToString();
            var newSession = new ChatSession(nextId, title, type, agentName);

            var updated = sessions.Value.Add(newSession);
            sessions.Set(updated);
            selectedIndex.Set(updated.Length - 1);

            if (makeDefault.Value)
            {
                configService.Settings.AlwaysUseDefaultChatType = true;
                configService.Settings.DefaultChatType = type;
                configService.SaveSettings();
            }
        }

        var tabs = sessions.Value.Select((s, index) =>
        {
            ViewBase content = s.Type == "CLI"
                ? new CliChatView(s.Id, s.AgentName)
                : new AgentChatView(s.Id, s.AgentName);

            return new Tab(s.Title, content).Icon(s.Type == "CLI" ? Icons.Terminal : Icons.MessageSquare);
        }).ToArray();

        var tabView = Layout.Tabs(tabs)
            .Variant(TabsVariant.Tabs)
            .RemoveParentPadding()
            .SelectedIndex(selectedIndex.Value)
            .OnSelect(index => selectedIndex.Set(index))
            .OnClose(index => HandleClose(index))
            .AddButton("+", () => HandleAddTabClick());

        var resetControl = configService.Settings.AlwaysUseDefaultChatType
            ? Layout.Horizontal(
                new Button("Reset Default Chat Type").Outline().Small().OnClick(() =>
                {
                    configService.Settings.AlwaysUseDefaultChatType = false;
                    configService.SaveSettings();
                    refreshTrigger.Set(refreshTrigger.Value + 1);
                })
              )
            : null;

        var mainLayout = resetControl != null
            ? (object)Layout.Vertical(resetControl, tabView).Full().RemoveParentPadding()
            : tabView.Height(Size.Full()).Width(Size.Full());

        return new Fragment(mainLayout, dialogView);
    }

    private static string[] GetCommandLine(IConfigService config, IAgentRunner runner, string? initialPrompt, string? agentName)
    {
        var agentId = agentName ?? config.Settings.CodingAgent;
        var cli = runner.GetCli(agentId);
        var pty = runner.GetPty(agentId);
        var workDir = GetDefaultWorkDir(config);
        var systemPrompt = AgentPromptCompiler.Compile(config);

        WriteAgentInstructionsIfNeeded(workDir, systemPrompt, pty);

        var model = pty?.Id == AgentId.Claude ? "default" : null;

        var spec = pty?.BuildPtySpec(new AgentPtyConfig
        {
            WorkingDirectory = workDir,
            PermissionMode = PermissionMode.FullAuto,
            SystemPrompt = systemPrompt,
            AppendSystemPrompt = true,
            Model = model,
            InitialPrompt = initialPrompt,
        });
        return spec?.ResolveCommand().CommandLine.ToArray() ?? [cli.Id];
    }

    private static void WriteAgentInstructionsIfNeeded(string workDir, string? systemPrompt, IAgentPty? pty)
    {
        if (string.IsNullOrEmpty(systemPrompt) || string.IsNullOrEmpty(workDir))
            return;

        var contextFile = pty?.ContextFileName;
        if (string.IsNullOrEmpty(contextFile))
            return;

        File.WriteAllText(Path.Combine(workDir, contextFile), systemPrompt);
    }

    private static string GetWorkDir(IConfigService config, IAgentRunner runner, string? agentName)
    {
        var agentId = agentName ?? config.Settings.CodingAgent;
        var pty = runner.GetPty(agentId);
        var defaultDir = GetDefaultWorkDir(config);
        var spec = pty?.BuildPtySpec(new AgentPtyConfig
        {
            WorkingDirectory = defaultDir,
            PermissionMode = PermissionMode.Default,
        });
        return spec?.WorkingDirectory ?? defaultDir;
    }

    private static Dictionary<string, string>? GetEnvironment(IConfigService config, string? agentName)
    {
        var env = new Dictionary<string, string>();

        var agentId = agentName ?? config.Settings.CodingAgent;
        var agentConfig = config.Settings.CodingAgents.FirstOrDefault(a =>
            AgentProviderFactory.NormalizeAgentName(a.Name).Equals(agentId, StringComparison.OrdinalIgnoreCase) ||
            a.Name.Equals(agentId, StringComparison.OrdinalIgnoreCase));
        if (agentConfig?.EnvironmentVariables is { Count: > 0 } d)
            foreach (var (key, value) in d)
                env[key] = value;

        AgentProcessHelper.ApplyTendrilEnvironment(env, config);

        return env.Count > 0 ? env : null;
    }

    private static string GetDefaultWorkDir(IConfigService config) =>
        !string.IsNullOrEmpty(config.TendrilHome)
            ? config.TendrilHome
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static AgentActivityPatterns? GetActivityPatterns(IConfigService config, IAgentRunner runner, string? agentName)
    {
        var agentId = agentName ?? config.Settings.CodingAgent;
        var pty = runner.GetPty(agentId);
        return pty?.GetActivityPatterns();
    }

    public class CliChatView(string sessionId, string? agentName = null) : ViewBase
    {
        public override object Build()
        {
            var configService = UseService<IConfigService>();
            var agentRunner = UseService<IAgentRunner>();
            var chatManager = UseService<IAgentChatManager>();
            var args = UseArgs<AgentAppArgs>();

            var trustHandled = UseRef(false);
            var trustBuffer = UseRef(new StringBuilder());
            var sendInput = UseRef<Action<string>?>(null);
            var trust = UseRef<(Regex? Regex, string Accept)>((null, "\r"));

            var ptyHandle = Context.UsePty(
                GetCommandLine(configService, agentRunner, args?.Prompt, agentName),
                GetWorkDir(configService, agentRunner, agentName),
                new PtyOptions
                {
                    Environment = GetEnvironment(configService, agentName),
                    OnOutput = text =>
                    {
                        var (regex, accept) = trust.Value;
                        if (regex == null || trustHandled.Value) return;
                        var sb = trustBuffer.Value;
                        sb.Append(text);
                        if (sb.Length > 8192) sb.Remove(0, sb.Length - 8192);
                        if (!regex.IsMatch(sb.ToString())) return;
                        var send = sendInput.Value;
                        if (send == null) return;
                        trustHandled.Value = true;
                        send(accept);
                    }
                }
            );

            UseEffect((Func<IDisposable?>)(() =>
            {
                return !ptyHandle.Closed
                    ? chatManager.RegisterActiveChat(sessionId)
                    : null;
            }));

            sendInput.Value = ptyHandle.HandleInput;
            var patterns = GetActivityPatterns(configService, agentRunner, agentName);
            trust.Value = (
                patterns?.TrustPromptPattern is { Length: > 0 } trustPattern
                    ? new Regex(trustPattern, RegexOptions.IgnoreCase)
                    : null,
                patterns?.TrustAcceptInput is { Length: > 0 } accept ? accept : "\r");

            var terminal = new Xterm.Terminal()
                .Stream(ptyHandle.Stream)
                .OnInput(ptyHandle.HandleInput)
                .OnResize(ptyHandle.HandleResize)
                .Closed(ptyHandle.Closed)
                .AllowClipboard()
                .Loading($"Starting {agentRunner.GetCli(agentName ?? configService.Settings.CodingAgent).DisplayName}...")
                .WithLayout()
                .Full()
                .RemoveParentPadding();

            return terminal;
        }
    }

    public class AgentChatView(string sessionId, string? agentName = null) : ViewBase
    {
        public override object Build()
        {
            var configService = UseService<IConfigService>();
            var agentRunner = UseService<IAgentRunner>();
            var chatManager = UseService<IAgentChatManager>();
            var promptwareRunner = UseService<IPromptwareRunner>();
            var messages = UseState(ImmutableArray.Create<AgentChatMessage>());
            var history = UseState(ImmutableArray.Create<string>());
            var isStreaming = UseState(false);
            var chatStream = UseStream<string>();
            var currentRunHandle = UseRef<PromptwareRunHandle?>(null);

            UseEffect((Func<IDisposable?>)(() =>
            {
                return isStreaming.Value
                    ? chatManager.RegisterActiveChat(sessionId)
                    : null;
            }), isStreaming);

            void HandleSend(Event<AgentChat, string> e)
            {
                var userText = e.Value;

                var updatedMessages = messages.Value
                    .Add(new AgentChatMessage("User", userText));
                messages.Set(updatedMessages);
                isStreaming.Set(true);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var promptBuilder = new StringBuilder();
                        foreach (var h in history.Value)
                        {
                            promptBuilder.AppendLine(h);
                        }
                        promptBuilder.AppendLine($"User: {userText}");
                        promptBuilder.AppendLine("\nResponse:");
                        var promptText = promptBuilder.ToString();

                        var activeAgent = agentName ?? configService.Settings.CodingAgent;
                        var workDir = GetWorkDir(configService, agentRunner, activeAgent);

                        var runOptions = new PromptwareRunOptions
                        {
                            Promptware = "AgentChat",
                            Values = new Dictionary<string, string>
                            {
                                ["InitialPrompt"] = promptText,
                            },
                            WorkingDir = workDir
                        };

                        var builder = new StringBuilder();
                        var compositeStream = new CompositeWriteStream(chatStream, builder);

                        var runHandle = promptwareRunner.Run(runOptions, compositeStream);
                        currentRunHandle.Value = runHandle;

                        await runHandle.Completion;

                        // Wait a tiny bit for the pipe task to flush
                        await Task.Delay(150);

                        var responseText = builder.ToString();
                        var finalizedMessages = messages.Value
                            .Add(new AgentChatMessage("Assistant", responseText));
                        messages.Set(finalizedMessages);

                        history.Set(history.Value
                            .Add($"User: {userText}")
                            .Add($"Agent: {responseText}"));
                    }
                    catch (Exception ex)
                    {
                        var errorMessages = messages.Value
                            .Add(new AgentChatMessage("Assistant", $"Error: {ex.Message}"));
                        messages.Set(errorMessages);
                    }
                    finally
                    {
                        isStreaming.Set(false);
                        currentRunHandle.Value?.Dispose();
                        currentRunHandle.Value = null;
                    }
                });
            }

            void HandleCancel(Event<AgentChat> e)
            {
                currentRunHandle.Value?.Cancel();
                isStreaming.Set(false);
            }

            return new AgentChat()
                .Messages(messages.Value.ToArray())
                .IsStreaming(isStreaming.Value)
                .Stream(chatStream)
                .OnSend(e => HandleSend(e))
                .OnCancel(e => HandleCancel(e))
                .Placeholder("Ask the agent anything...")
                .Height(Size.Full())
                .Width(Size.Full());
        }

        private class CompositeWriteStream(IWriteStream<string> target, StringBuilder builder) : IWriteStream<string>
        {
            public string Id => target.Id;
            public void Write(string data)
            {
                lock (builder)
                {
                    builder.Append(data);
                    if (!data.EndsWith('\n'))
                    {
                        builder.Append('\n');
                    }
                }
                target.Write(data);
            }
        }
    }
}
