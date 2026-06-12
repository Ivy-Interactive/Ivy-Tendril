using System.Text.RegularExpressions;
using Ivy.Hooks.Pty;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;
using Ivy.Tendril.Apps.Agent;
using Ivy.Tendril.Services;
using Ivy.Widgets.Xterm;
using Xterm = Ivy.Widgets.Xterm;

namespace Ivy.Tendril.Apps;

[App(title: "Agent", icon: Icons.Terminal, group: ["Apps"], order: Constants.Agent, isVisible: true, allowDuplicateTabs: true)]
public class AgentApp : ViewBase
{
    public override object Build()
    {
        var configService = UseService<IConfigService>();
        var agentRunner = UseService<IAgentRunner>();
        var args = UseArgs<AgentAppArgs>();

        var ptyHandle = Context.UsePty(
            GetCommandLine(configService, agentRunner),
            GetWorkDir(configService, agentRunner),
            new PtyOptions { Environment = GetEnvironment(configService) }
        );

        var promptSent = UseRef(false);

        // Auto-send the prompt by typing it into the PTY once the agent is ready.
        // Typing (rather than passing it as a CLI argument) keeps arbitrary content
        // intact — quotes, '#', newlines, pasted-image references. A separate Enter
        // keystroke is sent afterwards so the agent actually submits the prompt.
        Context.UseEffect(async () =>
        {
            if (string.IsNullOrEmpty(args?.Prompt) || promptSent.Value)
                return (IDisposable?)null;

            // Wait for agent to be ready (simple delay approach)
            await Task.Delay(2000); // 2 second delay

            if (!promptSent.Value)
            {
                promptSent.Value = true;
                ptyHandle.HandleInput(args.Prompt);

                // Submit with Enter. The text arrives as one chunk, so the agent's
                // paste handling can absorb an Enter that follows too quickly — give it
                // time to settle, then press Enter. A second Enter guards against the
                // first being swallowed; an extra Enter on an empty buffer is a no-op.
                await Task.Delay(700);
                ptyHandle.HandleInput("\r");
                await Task.Delay(300);
                ptyHandle.HandleInput("\r");
            }

            return (IDisposable?)null;
        }, EffectTrigger.OnMount());

        var terminal = new Xterm.Terminal()
            .Stream(ptyHandle.Stream)
            .OnInput(ptyHandle.HandleInput)
            .OnResize(ptyHandle.HandleResize)
            .Closed(ptyHandle.Closed)
            .AllowClipboard();

        return terminal
            .WithLayout()
            .Full()
            .RemoveParentPadding();
    }

    private static string[] GetCommandLine(IConfigService config, IAgentRunner runner)
    {
        var agentId = config.Settings.CodingAgent;
        var cli = runner.GetCli(agentId);
        var pty = runner.GetPty(agentId);
        var workDir = GetDefaultWorkDir(config);
        var systemPrompt = AgentPromptCompiler.Compile(config);

        WriteAgentInstructionsIfNeeded(workDir, systemPrompt, pty);

        // Claude takes "--model default" to explicitly select its configured default model.
        var model = pty?.Id == AgentId.Claude ? "default" : null;

        var spec = pty?.BuildPtySpec(new AgentPtyConfig
        {
            WorkingDirectory = workDir,
            PermissionMode = PermissionMode.FullAuto,
            SystemPrompt = systemPrompt,
            AppendSystemPrompt = true,
            Model = model,
        });
        return spec?.ResolveCommand().CommandLine.ToArray() ?? [cli.Id];
    }

    private static void WriteAgentInstructionsIfNeeded(string workDir, string? systemPrompt, IAgentPty? pty)
    {
        if (string.IsNullOrEmpty(systemPrompt) || string.IsNullOrEmpty(workDir))
            return;

        // Claude handles system prompt via --system-prompt / --append-system-prompt flags.
        // All other agents (OpenCode, Gemini, Codex, Copilot, Antigravity) read AGENTS.md from cwd.
        if (pty?.Id != AgentId.Claude)
        {
            var path = Path.Combine(workDir, "AGENTS.md");
            File.WriteAllText(path, systemPrompt);
        }
    }

    private static string GetWorkDir(IConfigService config, IAgentRunner runner)
    {
        var agentId = config.Settings.CodingAgent;
        var pty = runner.GetPty(agentId);
        var defaultDir = GetDefaultWorkDir(config);
        var spec = pty?.BuildPtySpec(new AgentPtyConfig
        {
            WorkingDirectory = defaultDir,
            PermissionMode = PermissionMode.Default,
        });
        return spec?.WorkingDirectory ?? defaultDir;
    }

    private static Dictionary<string, string>? GetEnvironment(IConfigService config)
    {
        var agentId = config.Settings.CodingAgent;
        var agentConfig = config.Settings.CodingAgents.FirstOrDefault(a =>
            AgentProviderFactory.NormalizeAgentName(a.Name).Equals(agentId, StringComparison.OrdinalIgnoreCase));
        return agentConfig?.EnvironmentVariables is { Count: > 0 } d
            ? new Dictionary<string, string>(d) : null;
    }

    private static string GetDefaultWorkDir(IConfigService config) =>
        !string.IsNullOrEmpty(config.TendrilHome)
            ? config.TendrilHome
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static AgentActivityPatterns? GetActivityPatterns(IConfigService config, IAgentRunner runner)
    {
        var agentId = config.Settings.CodingAgent;
        var pty = runner.GetPty(agentId);
        return pty?.GetActivityPatterns();
    }
}
