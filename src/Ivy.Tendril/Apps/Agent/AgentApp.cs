using System.Text;
using System.Text.RegularExpressions;
using Ivy.Hooks.Pty;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Ivy.Widgets.Xterm;
using Xterm = Ivy.Widgets.Xterm;

namespace Ivy.Tendril.Apps.Agent;

[App(title: "Agent", icon: Icons.Terminal, group: ["Apps"], order: Constants.Agent, isVisible: true, allowDuplicateTabs: true)]
public class AgentApp : ViewBase
{
    public override object Build()
    {
        var configService = UseService<IConfigService>();
        var agentRunner = UseService<IAgentRunner>();
        var args = UseArgs<AgentAppArgs>();

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
                Environment = AgentLaunchHelper.GetEnvironment(configService),
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

        // Wire the input sink + trust pattern now that the handle exists (OnOutput is supplied first).
        sendInput.Value = ptyHandle.HandleInput;
        var patterns = AgentLaunchHelper.GetActivityPatterns(configService, agentRunner);
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
            .Loading($"Starting {agentRunner.GetCli(configService.Settings.CodingAgent).DisplayName}...");

        return terminal
            .WithLayout()
            .Full()
            .RemoveParentPadding();
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
