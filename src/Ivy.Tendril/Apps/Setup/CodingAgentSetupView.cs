using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Setup;

public class CodingAgentSetupView : ViewBase
{
    private record AgentInfo(string Key, string Label, Icons Logo);

    private static readonly AgentInfo[] Agents =
    [
        new("claude", "Claude", Icons.ClaudeCode),
        new("copilot", "Copilot", Icons.Copilot),
        new("codex", "Codex", Icons.OpenAI),
        new("antigravity", "Antigravity", Icons.Antigravity),
        new("opencode", "OpenCode", Icons.OpenCode)
    ];

    public override object Build()
    {
        var config = UseService<IConfigService>();
        var client = UseService<IClientProvider>();

        var selectedAgent = UseState(
            string.IsNullOrWhiteSpace(config.Settings.CodingAgent)
                ? "claude"
                : config.Settings.CodingAgent);

        var grid = Layout.Grid().Columns(3).Gap(2);

        grid = Agents.Aggregate(grid, (current, a) =>
            current | new Card(
                Layout.Horizontal().Gap(2).AlignContent(Align.Center).Padding(0)
                | a.Logo.ToIcon().Width(Size.Px(32)).Height(Size.Px(32))
                | Text.Block(a.Label)
                | (a.Key == selectedAgent.Value ? Icons.Check.ToIcon() : null)
            ).Width(Size.Px(150)).OnClick(() =>
            {
                config.Settings.CodingAgent = a.Key;
                config.SaveSettings();
                selectedAgent.Set(a.Key);
                client.Toast($"Coding agent set to {a.Label}", "Saved");
            }));

        return Layout.Vertical().Padding(4).Width(Size.Auto().Max(Size.Units(120)))
               | Text.Block("Coding Agent").Bold()
               | Text.Muted("Pick the coding agent Tendril orchestrates:")
               | grid;
    }
}
