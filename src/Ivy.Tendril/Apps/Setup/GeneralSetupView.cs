using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Setup;

public class GeneralSetupView : ViewBase
{
    private static readonly string[] CodingAgentOptions = ["claude", "codex", "antigravity", "copilot", "opencode"];

    public override object Build()
    {
        var config = UseService<IConfigService>();
        var client = UseService<IClientProvider>();
        var codingAgent = UseState(string.IsNullOrWhiteSpace(config.Settings.CodingAgent)
            ? "claude"
            : config.Settings.CodingAgent);
        var planTemplate = UseState(config.Settings.PlanTemplate);
        var currentCodingAgent = string.IsNullOrWhiteSpace(config.Settings.CodingAgent)
            ? "claude"
            : config.Settings.CodingAgent;

        var hasChanges = codingAgent.Value != currentCodingAgent
                          || planTemplate.Value != config.Settings.PlanTemplate;

        var form = Layout.Vertical().Padding(4).Width(Size.Auto().Max(Size.Units(120)))
                   | Text.Block("General Settings").Bold()
                   | Text.Block("Configure the default coding agent and plan template.").Muted().Small()
                   | codingAgent.ToSelectInput(CodingAgentOptions)
                       .WithField().Label("Coding Agent")
                   | planTemplate.ToCodeInput("Plan template...")
                       .Language(Languages.Markdown)
                       .Height(Size.Units(80))
                       .WithField().Label("Plan Template")
                   | new Button("Save").Primary()
                       .Disabled(!hasChanges)
                       .OnClick(() =>
                       {
                           config.Settings.CodingAgent = codingAgent.Value;
                           config.Settings.PlanTemplate = planTemplate.Value;
                           config.SaveSettings();
                           client.Toast("Settings saved and applied", "Saved");
                       })
                   | Text.Block("Theme").Bold()
                   | Text.Block("Choose how Tendril appears. System matches your OS setting.").Muted().Small()
                   | (Layout.Horizontal().Gap(2)
                      | new Button("Light").Variant(ButtonVariant.Outline).Icon(Icons.Sun)
                          .OnClick(() => client.SetThemeMode(ThemeMode.Light))
                      | new Button("Dark").Variant(ButtonVariant.Outline).Icon(Icons.Moon)
                          .OnClick(() => client.SetThemeMode(ThemeMode.Dark))
                      | new Button("System").Variant(ButtonVariant.Outline).Icon(Icons.SunMoon)
                          .OnClick(() => client.SetThemeMode(ThemeMode.System)));

        return form;
    }
}
