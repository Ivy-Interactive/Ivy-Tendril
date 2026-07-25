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
        var rateLimitCooldown = UseState(config.Settings.RateLimitCooldown);
        var rateLimitDailyCooldown = UseState(config.Settings.RateLimitDailyCooldown);
        var rateLimitMaxRetries = UseState(config.Settings.RateLimitMaxRetries);
        var editorCommand = UseState(config.Settings.Editor.Command);
        var editorLabel = UseState(config.Settings.Editor.Label);

        var hasChanges = jobTimeout.Value != config.Settings.JobTimeout
                         || staleOutputTimeout.Value != config.Settings.StaleOutputTimeout
                         || maxConcurrentJobs.Value != config.Settings.MaxConcurrentJobs
                         || rateLimitCooldown.Value != config.Settings.RateLimitCooldown
                         || rateLimitDailyCooldown.Value != config.Settings.RateLimitDailyCooldown
                         || rateLimitMaxRetries.Value != config.Settings.RateLimitMaxRetries
                         || editorCommand.Value != config.Settings.Editor.Command
                         || editorLabel.Value != config.Settings.Editor.Label;

        var form = Layout.Vertical().Padding(4).Width(Size.Auto().Max(Size.Units(120)))
                   | Text.Block("Advanced Settings").Bold()
                   | Text.Block("Configure timeouts, concurrency limits, and editor preferences.").Muted().Small()
                   | Text.Block("Timeouts").Bold()
                   | jobTimeout.ToNumberInput().Min(1).Max(120).Suffix("min")
                       .WithField().Label("Job Timeout")
                   | staleOutputTimeout.ToNumberInput().Min(1).Max(60).Suffix("min")
                       .WithField().Label("Stale Output Timeout")
                   | maxConcurrentJobs.ToNumberInput().Min(1).Max(100)
                       .WithField().Label("Max Concurrent Jobs")
                   | Text.Block("Rate Limits").Bold()
                   | rateLimitCooldown.ToNumberInput().Min(1).Max(1440).Suffix("min")
                       .WithField().Label("Rate Limit Cooldown")
                       .Description("How long the job queue pauses after the provider rate limits a job.")
                   | rateLimitDailyCooldown.ToNumberInput().Min(1).Max(1440).Suffix("min")
                       .WithField().Label("Daily Quota Cooldown")
                       .Description("How long the job queue pauses after a daily token quota is exhausted.")
                   | rateLimitMaxRetries.ToNumberInput().Min(0).Max(10)
                       .WithField().Label("Rate Limit Max Retries")
                       .Description("Automatic retries per job. 0 fails rate-limited jobs immediately.")
                   | Text.Block("Editor").Bold()
                   | editorCommand.ToTextInput("e.g. code, vim")
                       .WithField().Label("Command")
                       .Description(!config.Editor.IsAvailable
                           ? $"⚠ '{config.Editor.Command}' was not found in PATH"
                           : null)
                   | editorLabel.ToTextInput("e.g. VS Code, Vim")
                       .WithField().Label("Label")
                   | new Button("Save").Primary()
                       .Disabled(!hasChanges)
                       .OnClick(() =>
                       {
                           config.Settings.JobTimeout = jobTimeout.Value;
                           config.Settings.StaleOutputTimeout = staleOutputTimeout.Value;
                           config.Settings.MaxConcurrentJobs = maxConcurrentJobs.Value;
                           config.Settings.RateLimitCooldown = rateLimitCooldown.Value;
                           config.Settings.RateLimitDailyCooldown = rateLimitDailyCooldown.Value;
                           config.Settings.RateLimitMaxRetries = rateLimitMaxRetries.Value;
                           config.Settings.Editor.Command = editorCommand.Value;
                           config.Settings.Editor.Label = editorLabel.Value;
                           config.SaveSettings();
                           client.Toast("Settings saved and applied", "Saved");
                       });

        return form;
    }
}
