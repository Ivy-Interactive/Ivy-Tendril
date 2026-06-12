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

        // Agents that accept the prompt as a positional CLI argument get it embedded
        // in the command line; the rest fall back to typing it into the PTY on launch.
        var promptInArgs = PromptPassedAsArg(configService, agentRunner);
        var initialPrompt = promptInArgs ? args?.Prompt : null;

        var ptyHandle = Context.UsePty(
            GetCommandLine(configService, agentRunner, initialPrompt),
            GetWorkDir(configService, agentRunner),
            new PtyOptions { Environment = GetEnvironment(configService) }
        );

        var promptSent = UseRef(false);

        // Auto-send prompt if provided and not already passed on the command line.
        Context.UseEffect(async () =>
        {
            if (promptInArgs || string.IsNullOrEmpty(args?.Prompt) || promptSent.Value)
                return (IDisposable?)null;

            // Wait for agent to be ready (simple delay approach)
            await Task.Delay(2000); // 2 second delay

            if (!promptSent.Value)
            {
                promptSent.Value = true;
                ptyHandle.HandleInput(args.Prompt + "\n");
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

    private static string[] GetCommandLine(IConfigService config, IAgentRunner runner, string? initialPrompt)
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
            InitialPrompt = initialPrompt,
            Model = model,
        });
        return spec?.ResolveCommand().CommandLine.ToArray() ?? [cli.Id];
    }

    // Claude accepts the initial prompt as a positional CLI argument; other agents
    // (OpenCode, Gemini, Codex, Copilot, Antigravity) receive it typed into the PTY.
    private static bool PromptPassedAsArg(IConfigService config, IAgentRunner runner)
    {
        var agentId = config.Settings.CodingAgent;
        return runner.GetPty(agentId)?.Id == AgentId.Claude;
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
