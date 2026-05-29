using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Apps.Settings.Dialogs;
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
        new("gemini", "Gemini", Icons.Gemini),
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
        var showTestDialog = UseState(false);

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
        var modelOptions = new[] { new Option<string>("Default", "default") }
            .Concat(models.Select(m => new Option<string>(m.DisplayName, m.Id)))
            .ToArray<IAnyOption>();

        var hasProfileChanges =
            deepModel.Value != GetProfileModel(config, selectedAgent.Value, "deep") ||
            balancedModel.Value != GetProfileModel(config, selectedAgent.Value, "balanced") ||
            quickModel.Value != GetProfileModel(config, selectedAgent.Value, "quick");

        var hasChanges = selectedAgent.Value != config.Settings.CodingAgent || hasProfileChanges;

        var registeredAgents = runner.RegisteredAgents;
        var visibleAgents = Agents.Where(a => registeredAgents.Contains(a.Key)).ToArray();

        var grid = Layout.Grid().Columns(3).Gap(2);
        grid = visibleAgents.Aggregate(grid, (current, a) =>
            current | new Card(
                Layout.Horizontal().Gap(2).Padding(0)
                | a.Logo.ToIcon().Width(Size.Px(32)).Height(Size.Px(32))
                | Text.Block(a.Label)
                | new Spacer()
                | (a.Key == selectedAgent.Value ? Icons.Check.ToIcon() : null)
            ).Width(Size.Full()).OnClick(() =>
            {
                selectedAgent.Set(a.Key);
            }));

        return Layout.Vertical().Padding(4)
               | Text.Block("Coding Agent").Bold()
               | grid.Width(Size.Units(170))
               | (Layout.Vertical().Width(Size.Auto().Max(Size.Units(120)))
                   | Text.Block("Profile Models").Bold()
                   | Text.Muted("Promptwares are configured to use different profiles depending on the complexity of the task. You can specify what model to use for each profile.").Small()
                   | deepModel.ToSelectInput(modelOptions).Loading(modelsQuery.Loading).WithField().Label("Deep")
                   | balancedModel.ToSelectInput(modelOptions).Loading(modelsQuery.Loading).WithField().Label("Balanced")
                   | quickModel.ToSelectInput(modelOptions).Loading(modelsQuery.Loading).WithField().Label("Quick"))
               | new Spacer().Height(Size.Units(4))
               | (Layout.Horizontal().Gap(2)
                   | new Button("Test Agent").Outline()
                       .Disabled(modelsQuery.Loading)
                       .OnClick(() => showTestDialog.Set(true))
                   | new Button("Save").Primary()
                       .Disabled(!hasChanges)
                       .OnClick(() =>
                       {
                           config.Settings.CodingAgent = selectedAgent.Value;
                           SaveProfiles(config, selectedAgent.Value, deepModel.Value, balancedModel.Value, quickModel.Value);
                           config.SaveSettings();
                           client.Toast("Coding agent settings saved", "Saved");
                       }))
               | new AgentTestDialog(
                   showTestDialog,
                   selectedAgent,
                   () =>
                   {
                       var currentModels = new[] { deepModel.Value, balancedModel.Value, quickModel.Value };
                       var entries = new List<TestModelEntry>();
                       var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                       foreach (var m in currentModels)
                       {
                           if (string.IsNullOrEmpty(m) || m.Equals("default", StringComparison.OrdinalIgnoreCase))
                           {
                               if (seen.Add("default"))
                                   entries.Add(new TestModelEntry(null, "Default"));
                           }
                           else
                           {
                               if (seen.Add(m))
                               {
                                   var displayName = models.FirstOrDefault(x => x.Id.Equals(m, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? m;
                                   entries.Add(new TestModelEntry(m, displayName));
                               }
                           }
                       }

                       return entries;
                   },
                   runner);
    }

    private static string GetProfileModel(IConfigService config, string agentId, string profileName)
    {
        var ac = config.Settings.CodingAgents.FirstOrDefault(a =>
            AgentProviderFactory.NormalizeAgentName(a.Name).Equals(agentId, StringComparison.OrdinalIgnoreCase));
        var profile = ac?.Profiles.FirstOrDefault(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));
        var value = profile?.Model;
        return string.IsNullOrEmpty(value) ? "default" : value;
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
