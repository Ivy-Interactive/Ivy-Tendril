using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Settings;

public class CodingAgentSetupView : ViewBase
{
    private record AgentInfo(string Key, string Label, Icons Logo);

    private enum TestStatus { Running, Passed, Failed, Warning }
    private record AgentTestResult(string Label, TestStatus Status, string? Message = null);

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
        var isTesting = UseState(false);
        var testResults = UseState<List<AgentTestResult>?>(null);

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
            testResults.Set(null);
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

        async Task RunTestsAsync()
        {
            isTesting.Set(true);
            var results = new List<AgentTestResult>();
            testResults.Set(results);

            try
            {
                var healthCheck = runner.GetHealthCheck(selectedAgent.Value);

                results.Add(new AgentTestResult("Installation", TestStatus.Running));
                testResults.Set([..results]);

                var installStatus = await healthCheck.CheckInstallAsync();
                results[^1] = installStatus.IsInstalled
                    ? new AgentTestResult("Installation", TestStatus.Passed, installStatus.Version != null ? $"v{installStatus.Version}" : "Installed")
                    : new AgentTestResult("Installation", TestStatus.Failed, installStatus.Error ?? "Not installed");
                testResults.Set([..results]);

                if (!installStatus.IsInstalled)
                {
                    isTesting.Set(false);
                    return;
                }

                results.Add(new AgentTestResult("Authentication", TestStatus.Running));
                testResults.Set([..results]);

                var authResult = await healthCheck.CheckAuthAsync();
                results[^1] = authResult.Status switch
                {
                    AuthStatus.Authenticated => new AgentTestResult("Authentication", TestStatus.Passed, "Authenticated"),
                    AuthStatus.NotAuthenticated => new AgentTestResult("Authentication", TestStatus.Failed, "Not authenticated"),
                    _ => new AgentTestResult("Authentication", TestStatus.Warning, authResult.Error ?? "Check inconclusive")
                };
                testResults.Set([..results]);

                var modelValues = new[] { deepModel.Value, balancedModel.Value, quickModel.Value }
                    .Where(m => !string.IsNullOrEmpty(m) && !m.Equals("default", StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var model in modelValues)
                {
                    results.Add(new AgentTestResult($"Model: {model}", TestStatus.Running));
                    testResults.Set([..results]);

                    var modelResult = await healthCheck.ValidateModelAsync(model);
                    results[^1] = modelResult.Status switch
                    {
                        ModelValidationStatus.Ok => new AgentTestResult($"Model: {model}", TestStatus.Passed),
                        ModelValidationStatus.InvalidModel => new AgentTestResult($"Model: {model}", TestStatus.Failed, modelResult.ErrorMessage ?? "Invalid model"),
                        ModelValidationStatus.AuthError => new AgentTestResult($"Model: {model}", TestStatus.Failed, modelResult.ErrorMessage ?? "Auth error"),
                        ModelValidationStatus.Timeout => new AgentTestResult($"Model: {model}", TestStatus.Warning, "Timed out"),
                        _ => new AgentTestResult($"Model: {model}", TestStatus.Warning, modelResult.ErrorMessage ?? "Unknown")
                    };
                    testResults.Set([..results]);
                }
            }
            catch
            {
                results.Add(new AgentTestResult("Unexpected error", TestStatus.Failed, "Test run failed"));
                testResults.Set([..results]);
            }

            isTesting.Set(false);
        }

        return Layout.Vertical().Padding(4).Width(Size.Auto().Max(Size.Units(120)))
               | Text.Block("Coding Agent").Bold()
               | Text.Muted("Pick the coding agent Tendril orchestrates:")
               | grid
               | Text.Block("Model Per Profile").Bold()
               | Text.Muted("Override the model used for each profile tier:")
               | deepModel.ToSelectInput(modelOptions).Loading(modelsQuery.Loading).WithField().Label("Deep")
               | balancedModel.ToSelectInput(modelOptions).Loading(modelsQuery.Loading).WithField().Label("Balanced")
               | quickModel.ToSelectInput(modelOptions).Loading(modelsQuery.Loading).WithField().Label("Quick")
               | new Separator()
               | (Layout.Horizontal().Gap(2)
                   | new Button("Test Agent").Outline()
                       .Loading(isTesting.Value)
                       .Disabled(isTesting.Value || modelsQuery.Loading)
                       .OnClick(async () => await RunTestsAsync())
                   | new Button("Save").Primary()
                       .Disabled(!hasChanges)
                       .OnClick(() =>
                       {
                           config.Settings.CodingAgent = selectedAgent.Value;
                           SaveProfiles(config, selectedAgent.Value, deepModel.Value, balancedModel.Value, quickModel.Value);
                           config.SaveSettings();
                           client.Toast("Coding agent settings saved", "Saved");
                       }))
               | BuildTestResults(testResults.Value);
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

    private static object? BuildTestResults(List<AgentTestResult>? results)
    {
        if (results is not { Count: > 0 }) return null;

        var layout = Layout.Vertical().Gap(1);
        foreach (var r in results)
        {
            var (icon, color) = r.Status switch
            {
                TestStatus.Passed => (Icons.CircleCheck, Colors.Success),
                TestStatus.Failed => (Icons.CircleX, Colors.Destructive),
                TestStatus.Warning => (Icons.Info, Colors.Warning),
                _ => (Icons.RotateCw, Colors.Muted)
            };

            layout |= Layout.Horizontal().Gap(2).AlignContent(Align.Center)
                      | icon.ToIcon().Color(color)
                      | Text.Block(r.Label)
                      | (r.Message != null ? Text.Muted(r.Message) : null);
        }

        return layout;
    }
}
