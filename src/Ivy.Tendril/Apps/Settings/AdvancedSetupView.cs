using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Apps.Settings;

public class AdvancedSetupView : ViewBase
{
    public override object Build()
    {
        var config = UseService<IConfigService>();
        var client = UseService<IClientProvider>();

        var jobTimeout = UseState(config.Settings.JobTimeout);
        var staleOutputTimeout = UseState(config.Settings.StaleOutputTimeout);
        var maxConcurrentJobs = UseState(config.Settings.MaxConcurrentJobs);
        var editorCommand = UseState(config.Settings.Editor.Command);
        var editorLabel = UseState(config.Settings.Editor.Label);
        var beta = UseState(config.Settings.Beta);

        var hasChanges = jobTimeout.Value != config.Settings.JobTimeout
                         || staleOutputTimeout.Value != config.Settings.StaleOutputTimeout
                         || maxConcurrentJobs.Value != config.Settings.MaxConcurrentJobs
                         || editorCommand.Value != config.Settings.Editor.Command
                         || editorLabel.Value != config.Settings.Editor.Label
                         || beta.Value != config.Settings.Beta;

        var form = Layout.Vertical().Width(Size.Auto().Max(Size.Units(120)))
                   | Text.Block("Advanced Settings").Bold()
                   | Text.Block("Configure timeouts, concurrency limits, and editor preferences.").Muted().Small()
                   | Text.Block("Timeouts").Bold()
                   | jobTimeout.ToNumberInput().Min(1).Max(120).Suffix("min")
                       .WithField().Label("Job Timeout")
                   | staleOutputTimeout.ToNumberInput().Min(1).Max(60).Suffix("min")
                       .WithField().Label("Stale Output Timeout")
                   | maxConcurrentJobs.ToNumberInput().Min(1).Max(512)
                       .WithField().Label("Max Concurrent Jobs")
                   | Text.Block("Editor").Bold()
                   | editorCommand.ToTextInput("e.g. code, vim")
                       .WithField().Label("Command")
                       .Description(!config.Editor.IsAvailable
                           ? $"⚠ '{config.Editor.Command}' was not found in PATH"
                           : null)
                   | editorLabel.ToTextInput("e.g. VS Code, Vim")
                       .WithField().Label("Label")
                   | Text.Block("Beta Features").Bold()
                   | beta.ToSwitchInput()
                       .WithField().Label("Opt-in to beta features")
                   | Text.Muted("A Tendril restart is required for changes to take effect.").Small()
                   | new Button("Save").Primary()
                       .Disabled(!hasChanges)
                       .OnClick(() =>
                       {
                           config.Settings.JobTimeout = jobTimeout.Value;
                           config.Settings.StaleOutputTimeout = staleOutputTimeout.Value;
                           config.Settings.MaxConcurrentJobs = maxConcurrentJobs.Value;
                           config.Settings.Editor.Command = editorCommand.Value;
                           config.Settings.Editor.Label = editorLabel.Value;
                           config.Settings.Beta = beta.Value;
                           if (beta.Value)
                           {
                               Environment.SetEnvironmentVariable("TENDRIL_BETA", "1");
                           }
                           else
                           {
                               Environment.SetEnvironmentVariable("TENDRIL_BETA", null);
                           }
                           config.SaveSettings();
                           client.Toast("Settings saved and applied", "Saved");
                       });

        return form;
    }
}
