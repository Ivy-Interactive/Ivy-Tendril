using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Settings;

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
        var runner = UseService<IAgentRunner>();

        var selectedAgent = UseState(
            string.IsNullOrWhiteSpace(config.Settings.CodingAgent)
                ? "claude"
                : config.Settings.CodingAgent);

        var deepModel = UseState(GetProfileModel(config, selectedAgent.Value, "deep"));
        var balancedModel = UseState(GetProfileModel(config, selectedAgent.Value, "balanced"));
        var quickModel = UseState(GetProfileModel(config, selectedAgent.Value, "quick"));
        var lastAgent = UseState(selectedAgent.Value);

        var modelsQuery = UseQuery<ModelInfo[], string>(
            selectedAgent.Value,
            async (agentId, ct) =>
            {
                var catalog = runner.GetModelCatalog(agentId);
                if (catalog is null) return [];
                var result = await catalog.GetModelsAsync(ct);
                return result.Models.ToArray();
            },
            initialValue: []
        );

        if (lastAgent.Value != selectedAgent.Value)
        {
            deepModel.Set(GetProfileModel(config, selectedAgent.Value, "deep"));
            balancedModel.Set(GetProfileModel(config, selectedAgent.Value, "balanced"));
            quickModel.Set(GetProfileModel(config, selectedAgent.Value, "quick"));
            lastAgent.Set(selectedAgent.Value);
        }

        var models = modelsQuery.Value ?? [];
        var modelOptions = new[] { new Option<string>("Default", "") }
            .Concat(models.Select(m => new Option<string>(m.DisplayName, m.Id)))
            .ToArray<IAnyOption>();

        var hasProfileChanges =
            deepModel.Value != GetProfileModel(config, selectedAgent.Value, "deep") ||
            balancedModel.Value != GetProfileModel(config, selectedAgent.Value, "balanced") ||
            quickModel.Value != GetProfileModel(config, selectedAgent.Value, "quick");

        var hasChanges = selectedAgent.Value != config.Settings.CodingAgent || hasProfileChanges;

        var grid = Layout.Grid().Columns(3).Gap(2);
        grid = Agents.Aggregate(grid, (current, a) =>
            current | new Card(
                Layout.Horizontal().Gap(2).AlignContent(Align.Center).Padding(0)
                | a.Logo.ToIcon().Width(Size.Px(32)).Height(Size.Px(32))
                | Text.Block(a.Label)
                | (a.Key == selectedAgent.Value ? Icons.Check.ToIcon() : null)
            ).Width(Size.Px(200)).OnClick(() =>
            {
                selectedAgent.Set(a.Key);
            }));

        return Layout.Vertical().Padding(4).Width(Size.Auto().Max(Size.Units(120)))
               | Text.Block("Coding Agent").Bold()
               | Text.Muted("Pick the coding agent Tendril orchestrates:")
               | grid
               | Text.Block("Model Per Profile").Bold()
               | Text.Muted("Override the model used for each profile tier:")
               | deepModel.ToSelectInput(modelOptions).Loading(modelsQuery.Loading).WithField().Label("Deep")
               | balancedModel.ToSelectInput(modelOptions).Loading(modelsQuery.Loading).WithField().Label("Balanced")
               | quickModel.ToSelectInput(modelOptions).Loading(modelsQuery.Loading).WithField().Label("Quick")
               | new Button("Save").Primary()
                   .Disabled(!hasChanges)
                   .OnClick(() =>
                   {
                       config.Settings.CodingAgent = selectedAgent.Value;
                       SaveProfiles(config, selectedAgent.Value, deepModel.Value, balancedModel.Value, quickModel.Value);
                       config.SaveSettings();
                       client.Toast("Coding agent settings saved", "Saved");
                   });
    }

    private static string GetProfileModel(IConfigService config, string agentId, string profileName)
    {
        var ac = config.Settings.CodingAgents.FirstOrDefault(a =>
            AgentProviderFactory.NormalizeAgentName(a.Name).Equals(agentId, StringComparison.OrdinalIgnoreCase));
        var profile = ac?.Profiles.FirstOrDefault(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));
        return profile?.Model ?? "";
    }

    private static void SaveProfiles(IConfigService config, string agentId, string deep, string balanced, string quick)
    {
        var ac = config.Settings.CodingAgents.FirstOrDefault(a =>
            AgentProviderFactory.NormalizeAgentName(a.Name).Equals(agentId, StringComparison.OrdinalIgnoreCase));

        if (ac == null)
        {
            ac = new AgentConfig { Name = agentId };
            config.Settings.CodingAgents.Add(ac);
        }

        SetProfileModel(ac, "deep", deep);
        SetProfileModel(ac, "balanced", balanced);
        SetProfileModel(ac, "quick", quick);
    }

    private static void SetProfileModel(AgentConfig ac, string profileName, string model)
    {
        var profile = ac.Profiles.FirstOrDefault(p =>
            p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));

        if (profile == null)
        {
            profile = new AgentProfileConfig { Name = profileName };
            ac.Profiles.Add(profile);
        }

        profile.Model = model;
    }
}
