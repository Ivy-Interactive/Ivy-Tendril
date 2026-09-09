using System.Reactive.Disposables;
using System.Text;
using System.Text.RegularExpressions;
using Ivy.Hooks.Pty;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;
using Ivy.Tendril.Apps.Chat;
using Ivy.Tendril.Apps.Chat.Dialogs;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Widgets;
using Ivy.Widgets.Xterm;
using Xterm = Ivy.Widgets.Xterm;

namespace Ivy.Tendril.Apps.Agent;

[App(title: "Agent", icon: Icons.Terminal, group: ["Apps"], order: Constants.Agent, isVisible: true, allowDuplicateTabs: true)]
public class AgentApp : ViewBase
{
    private const string ReviewJobsPrompt = "All spawned jobs have completed. Please review their outcomes with me and suggest next steps.";

    public override object Build()
    {
        var configService = UseService<IConfigService>();
        var agentRunner = UseService<IAgentRunner>();
        var chatService = UseService<IChatHistoryService>();
        Context.TryUseService<IJobService>(out var jobService);
        var navigator = UseNavigation();
        var args = UseArgs<AgentAppArgs>();
        var sessionVersion = UseState(0);
        var deletingSessionId = UseState<string?>(null);
        var headerKey = UseRef<string?>(null);

        // The initial task is delivered as a command-line argument (see each provider's
        // BuildPtySpec) so the agent auto-runs it on launch — no fragile "wait then paste"
        // and no Windows arg-quoting issues (argv is passed as an array). The only thing we
        // still drive from here is the first-run "trust this folder?" modal that some agents
        // (Copilot, Codex) show before they accept the queued prompt.
        //
        // Ivy hooks must come first (IVYHOOK005), so the trust regex/keystroke are stashed in a
        // ref populated after UsePty; OnOutput fires asynchronously, by which point it is set.
        var trustHandled = UseRef(false);
        var trustBuffer = UseRef(new StringBuilder());
        var sendInput = UseRef<Action<string>?>(null);
        var trust = UseRef<(Regex? Regex, string Accept)>((null, "\r"));

        var ptyHandle = Context.UsePty(
            GetCommandLine(configService, agentRunner, args?.Prompt),
            AgentLaunchHelper.GetWorkDir(configService, agentRunner),
            new PtyOptions
            {
                Environment = BuildEnvironment(configService, args?.SessionId),
                OnOutput = text =>
                {
                    var (regex, accept) = trust.Value;
                    if (regex == null || trustHandled.Value) return;
                    // Keep a small rolling window of recent output; accept on first match.
                    var sb = trustBuffer.Value;
                    sb.Append(text);
                    if (sb.Length > 8192) sb.Remove(0, sb.Length - 8192);
                    if (!regex.IsMatch(sb.ToString())) return;
                    var send = sendInput.Value;
                    if (send == null) return; // PTY not wired yet; retry on next chunk
                    trustHandled.Value = true;
                    send(accept);
                }
            }
        );

        // Both events fire for every session and job in the process; only this pane's own
        // title and jobs are worth a rebuild.
        UseEffect(() =>
        {
            void Refresh()
            {
                var key = HeaderKey(chatService, jobService, args?.SessionId);
                if (key == headerKey.Value) return;
                headerKey.Value = key;
                sessionVersion.Set(v => v + 1);
            }

            void OnSessionsChanged(object? sender, EventArgs e) => Refresh();
            void OnJobsChanged() => Refresh();

            chatService.SessionsChanged += OnSessionsChanged;
            if (jobService != null) jobService.JobsChanged += OnJobsChanged;
            return Disposable.Create(() =>
            {
                chatService.SessionsChanged -= OnSessionsChanged;
                if (jobService != null) jobService.JobsChanged -= OnJobsChanged;
            });
        });

        // Wire the input sink + trust pattern now that the handle exists (OnOutput is supplied first).
        sendInput.Value = ptyHandle.HandleInput;
        var patterns = AgentLaunchHelper.GetActivityPatterns(configService, agentRunner);
        trust.Value = (
            patterns?.TrustPromptPattern is { Length: > 0 } trustPattern
                ? new Regex(trustPattern, RegexOptions.IgnoreCase)
                : null,
            patterns?.TrustAcceptInput is { Length: > 0 } accept ? accept : "\r");

        _ = sessionVersion.Value;
        var session = !string.IsNullOrEmpty(args?.SessionId) ? chatService.GetSession(args.SessionId) : null;
        var (agentLabel, _) = AgentBranding.For(configService.Settings.CodingAgent, agentRunner, configService);
        var title = session != null ? ChatApp.DisplayTitle(session) : (args?.Title ?? agentLabel);

        var jobs = session != null
            ? SessionJobs(jobService, session.Id).Select(ChatApp.ToJobDto).ToList()
            : [];

        var header = new TerminalSessionHeader()
            .SessionId(session?.Id ?? "")
            .Title(title)
            .Jobs(jobs)
            .Spawned(session?.SpawnedJobIds is { Count: > 0 })
            .OnRenameSession((id, newTitle) =>
            {
                chatService.RenameSession(id, newTitle);
                sessionVersion.Set(v => v + 1);
            })
            .OnDeleteSession(id => deletingSessionId.Set(id))
            .OnCreateSession(() => ChatLauncher.StartNew(navigator, configService, chatService, agentRunner))
            .OnReviewJobs(() => ptyHandle.HandleInput(ReviewJobsPrompt + "\r"));

        var deleteDialog = new DeleteSessionDialog(deletingSessionId, session, chatService, null, sessionVersion);

        var terminal = new Xterm.Terminal()
            .Stream(ptyHandle.Stream)
            .OnInput(ptyHandle.HandleInput)
            .OnResize(ptyHandle.HandleResize)
            .Closed(ptyHandle.Closed)
            .AllowClipboard()
            .Loading($"Starting {agentLabel}...")
            .Height(Size.Full());

        var pane = Layout.Vertical().Gap(0).Full().RemoveParentPadding()
                   | header
                   | terminal;

        return new Fragment(pane, deleteDialog);
    }

    private static IEnumerable<JobItem> SessionJobs(IJobService? jobService, string sessionId) =>
        jobService?.GetJobs().Where(j => string.Equals(j.ChatSessionId, sessionId, StringComparison.OrdinalIgnoreCase)) ?? [];

    internal static string HeaderKey(IChatHistoryService chatService, IJobService? jobService, string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return "";
        var session = chatService.GetSession(sessionId);
        var jobs = string.Join(",", SessionJobs(jobService, sessionId).Select(j => j.Id + ":" + j.Status));
        return $"{session?.Title}|{session?.SpawnedJobIds?.Count ?? 0}|{jobs}";
    }

    private static Dictionary<string, string> BuildEnvironment(IConfigService config, string? sessionId)
    {
        var env = AgentLaunchHelper.GetEnvironment(config);
        if (!string.IsNullOrEmpty(sessionId)) env["TENDRIL_CHAT_SESSION_ID"] = sessionId;
        return env;
    }

    private static string[] GetCommandLine(IConfigService config, IAgentRunner runner, string? initialPrompt)
    {
        var agentId = config.Settings.CodingAgent;
        var cli = runner.GetCli(agentId);
        var pty = runner.GetPty(agentId);
        var workDir = AgentLaunchHelper.GetDefaultWorkDir(config);
        var systemPrompt = AgentLaunchHelper.CompileSystemPrompt(config);

        AgentLaunchHelper.WriteAgentInstructionsIfNeeded(workDir, systemPrompt, pty);

        var model = AgentLaunchHelper.ResolveModel(config, runner, agentId);

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
}
