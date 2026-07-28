using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Settings;

public class PlansSetupView : ViewBase
{
    public override object Build()
    {
        var config = UseService<IConfigService>();
        var client = UseService<IClientProvider>();
        var planTemplate = UseState(config.Settings.PlanTemplate);

        var jobTimeout = UseState(config.Settings.JobTimeout);
        var staleOutputTimeout = UseState(config.Settings.StaleOutputTimeout);
        var maxConcurrentJobs = UseState(config.Settings.MaxConcurrentJobs);

        var hasChanges = planTemplate.Value != config.Settings.PlanTemplate
                         || jobTimeout.Value != config.Settings.JobTimeout
                         || staleOutputTimeout.Value != config.Settings.StaleOutputTimeout
                         || maxConcurrentJobs.Value != config.Settings.MaxConcurrentJobs;

        return Layout.Vertical().Padding(4).Width(Size.Auto().Max(Size.Units(120)))
               | Text.Block("Plans").Bold()
               | Text.Muted("Configure the default plan template used when creating new plans.").Small()
               | planTemplate.ToCodeInput("Plan template...")
                   .Language(Languages.Markdown)
                   .Height(Size.Units(80))
                   .WithField().Label("Plan Template")
               | Text.Block("Job Execution & Concurrency").Bold()
               | jobTimeout.ToNumberInput().Min(1).Max(120).Suffix("min")
                   .WithField().Label("Job Timeout")
               | staleOutputTimeout.ToNumberInput().Min(1).Max(60).Suffix("min")
                   .WithField().Label("Stale Output Timeout")
               | maxConcurrentJobs.ToNumberInput().Min(1).Max(100)
                   .WithField().Label("Max Concurrent Jobs")
               | new Button("Save Settings").Primary()
                   .Disabled(!hasChanges)
                   .OnClick(() =>
                   {
                       config.Settings.PlanTemplate = planTemplate.Value;
                       config.Settings.JobTimeout = jobTimeout.Value;
                       config.Settings.StaleOutputTimeout = staleOutputTimeout.Value;
                       config.Settings.MaxConcurrentJobs = maxConcurrentJobs.Value;
                       config.SaveSettings();
                       client.Toast("Plan settings saved and applied", "Saved");
                   });
    }
}
