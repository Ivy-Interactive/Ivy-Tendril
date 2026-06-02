using Ivy.Hooks.Pty;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;
using Ivy.Tendril.Services;
using Ivy.Widgets.Xterm;
using Xterm = Ivy.Widgets.Xterm;

namespace Ivy.Tendril.Apps;

[App(title: "Agent", icon: Icons.Terminal, group: ["Apps"], order: Constants.Agent, isVisible: true, allowDuplicateTabs:true)]
public class AgentApp : ViewBase
{
    public override object Build()
    {
        var configService = UseService<IConfigService>();
        var agentRunner = UseService<IAgentRunner>();
        
        var ptyHandle = Context.UsePty(
            GetCommandLine(configService, agentRunner),
            GetWorkDir(configService, agentRunner)
        );
        
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

        var spec = pty?.BuildPtySpec(new AgentPtyConfig
        {
            WorkingDirectory = workDir,
            PermissionMode = PermissionMode.Default,
            SystemPrompt = systemPrompt,
            AppendSystemPrompt = true,
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

    private static string GetDefaultWorkDir(IConfigService config) =>
        !string.IsNullOrEmpty(config.TendrilHome)
            ? config.TendrilHome
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
