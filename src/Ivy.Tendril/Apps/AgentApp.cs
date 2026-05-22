using Ivy.Hooks.Pty;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Services;
using Xterm = Ivy.Widgets.Xterm;

namespace Ivy.Tendril.Apps;

[App(title: "Agent", icon: Icons.Terminal, group: ["Apps"], order: Constants.Agent, isVisible: false)]
public class AgentApp : ViewBase
{
    public override object Build()
    {
        var isOpen = UseState(false);
        var configService = UseService<IConfigService>();
        var agentRunner = UseService<IAgentRunner>();
#pragma warning disable IVYHOOK005
        var ptyHandle = Context.UsePty(
            GetCommandLine(configService, agentRunner),
            GetWorkDir(configService, agentRunner));
#pragma warning restore IVYHOOK005

        var cli = agentRunner.GetCli(configService.Settings.CodingAgent);

        if (!isOpen.Value)
            return new Button($"Open {cli.DisplayName}")
                .OnClick(() => isOpen.Set(true));

        var terminal = new Xterm.Terminal();
        terminal = Xterm.TerminalExtensions.Stream(terminal, ptyHandle.Stream);
        terminal = Xterm.TerminalExtensions.OnInput(terminal, ptyHandle.HandleInput);
        terminal = Xterm.TerminalExtensions.OnResize(terminal, ptyHandle.HandleResize);
        terminal = Xterm.TerminalExtensions.Closed(terminal, ptyHandle.Closed);
        terminal = Xterm.TerminalExtensions.AllowClipboard(terminal);

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
        var spec = pty?.BuildPtySpec(new AgentPtyConfig
        {
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            PermissionMode = PermissionMode.Default,
        });
        return spec?.CommandLine.ToArray() ?? [cli.Id];
    }

    private static string GetWorkDir(IConfigService config, IAgentRunner runner)
    {
        var agentId = config.Settings.CodingAgent;
        var pty = runner.GetPty(agentId);
        var defaultDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var spec = pty?.BuildPtySpec(new AgentPtyConfig
        {
            WorkingDirectory = defaultDir,
            PermissionMode = PermissionMode.Default,
        });
        return spec?.WorkingDirectory ?? defaultDir;
    }
}
