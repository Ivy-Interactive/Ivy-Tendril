using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Setup;

public class PlansSetupView : ViewBase
{
    public override object Build()
    {
        var config = UseService<IConfigService>();
        var client = UseService<IClientProvider>();
        var planTemplate = UseState(config.Settings.PlanTemplate);

        var hasChanges = planTemplate.Value != config.Settings.PlanTemplate;

        return Layout.Vertical().Padding(4).Width(Size.Auto().Max(Size.Units(120)))
               | Text.Block("Plans").Bold()
               | Text.Muted("Configure the default plan template used when creating new plans.").Small()
               | planTemplate.ToCodeInput("Plan template...")
                   .Language(Languages.Markdown)
                   .Height(Size.Units(80))
                   .WithField().Label("Plan Template")
               | new Button("Save").Primary()
                   .Disabled(!hasChanges)
                   .OnClick(() =>
                   {
                       config.Settings.PlanTemplate = planTemplate.Value;
                       config.SaveSettings();
                       client.Toast("Plan template saved", "Saved");
                   });
    }
}
