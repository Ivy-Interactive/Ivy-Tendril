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

        var ptyHandle = Context.UsePty(
            GetCommandLine(configService, agentRunner),
            GetWorkDir(configService, agentRunner),
            new PtyOptions { Environment = GetEnvironment(configService) }
        );

        var promptSent = UseRef(false);

        // Deliver the prompt once the agent is ready. We type it in (rather than pass it
        // as a CLI argument) because Windows command-line quoting mangles prompts that
        // contain quotes or backslash paths. Sending it as a bracketed paste lets the
        // agent ingest arbitrary content atomically, then Enter submits it. A second
        // Enter guards against the first being swallowed (a no-op on an empty buffer).
        Context.UseEffect(async () =>
        {
            if (string.IsNullOrEmpty(args?.Prompt) || promptSent.Value)
                return (IDisposable?)null;

            // Wait for agent to be ready (simple delay approach)
            await Task.Delay(2000); // 2 second delay

            if (!promptSent.Value)
            {
                promptSent.Value = true;
                ptyHandle.HandleInput("\u001b[200~" + args.Prompt + "\u001b[201~");
                // The paste takes a moment to settle before the agent accepts an Enter
                // as "submit" (an Enter sent too early is absorbed). Retry over a longer
                // window; once submitted, further Enters hit an empty buffer and no-op.
                for (var i = 0; i < 3; i++)
                {
                    await Task.Delay(800);
                    ptyHandle.HandleInput("\r");
                }
            }

            return (IDisposable?)null;
        }, EffectTrigger.OnMount());

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
        var env = new Dictionary<string, string>();

        var agentId = config.Settings.CodingAgent;
        var agentConfig = config.Settings.CodingAgents.FirstOrDefault(a =>
            AgentProviderFactory.NormalizeAgentName(a.Name).Equals(agentId, StringComparison.OrdinalIgnoreCase));
        if (agentConfig?.EnvironmentVariables is { Count: > 0 } d)
            foreach (var (key, value) in d)
                env[key] = value;

        // Expose the `tendril` CLI (via shim) and the active config/plans to the agent running in
        // the PTY, so it can run `tendril ...` even when no tendril binary is installed (e.g. in dev).
        AgentProcessHelper.ApplyTendrilEnvironment(env, config);

        return env.Count > 0 ? env : null;
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
