using System.Reactive.Disposables;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Slack;

namespace Ivy.Tendril.Apps.Settings;

public class SlackbotSetupView : ViewBase
{
    public override object Build()
    {
        var config = UseService<IConfigService>();
        var client = UseService<IClientProvider>();
        var slackbot = UseService<ISlackbotService>();

        var enabled = UseState(() => config.Settings.Slackbot?.Enabled ?? false);
        var botToken = UseState(() => config.Settings.Slackbot?.BotToken ?? "");
        var appToken = UseState(() => config.Settings.Slackbot?.AppToken ?? "");
        var project = UseState(() =>
        {
            var configured = config.Settings.Slackbot?.Project;
            return string.IsNullOrWhiteSpace(configured) ? "Auto" : configured;
        });
        var status = UseState(() => slackbot.Status);
        var saving = UseState(false);

        UseEffect(() =>
        {
            void OnStatusChanged(SlackbotStatus newStatus) => status.Set(newStatus);

            slackbot.StatusChanged += OnStatusChanged;
            status.Set(slackbot.Status);

            return Disposable.Create(() => slackbot.StatusChanged -= OnStatusChanged);
        });

        var saved = config.Settings.Slackbot;

        var projectOptions = new[] { "Auto" }
            .Concat(config.Settings.Projects.Select(p => p.Name))
            .Distinct()
            .ToOptions();

        var hasChanges = enabled.Value != (saved?.Enabled ?? false)
                         || botToken.Value != (saved?.BotToken ?? "")
                         || appToken.Value != (saved?.AppToken ?? "")
                         || project.Value != (string.IsNullOrWhiteSpace(saved?.Project) ? "Auto" : saved!.Project);

        var tokensMissing = string.IsNullOrWhiteSpace(botToken.Value) || string.IsNullOrWhiteSpace(appToken.Value);

        var form = Layout.Vertical().Padding(4).Width(Size.Auto().Max(Size.Units(120)))
                   | Text.Block("Slackbot").Bold()
                   | Text.Block(
                       "Install a Tendril bot in your Slack workspace. Mention the bot in any channel it has been invited to and it creates a Tendril plan from your message, using the recent conversation as context, and replies in the thread when the plan is ready.")
                       .Muted().Small();

        if (enabled.Value && (saved?.Enabled ?? false))
        {
            if (status.Value == SlackbotStatus.Connected)
            {
                var connectedContent = Layout.Vertical()
                    | Text.Block($"Connected as @{slackbot.BotName} in {slackbot.TeamName}.")
                    | Text.Block($"Invite the bot to a channel with /invite @{slackbot.BotName}, then mention it with a task, e.g. \"@{slackbot.BotName} fix the flaky login test\".")
                        .Muted().Small();
                form |= new Callout(connectedContent, "Slackbot Active", CalloutVariant.Success);
            }
            else if (status.Value == SlackbotStatus.Connecting)
            {
                var connectingContent = Layout.Vertical()
                    | Text.Block("Connecting to Slack...")
                    | new Loading();
                form |= Callout.Info(connectingContent, "Slackbot Connecting");
            }
            else if (status.Value == SlackbotStatus.Error)
            {
                form |= Callout.Error(slackbot.LastError ?? "Unknown error", "Slackbot Error");
            }
        }

        form |= Text.Block("Step 1: Create the Slack app").Bold();
        form |= Text.Block(
                "This opens Slack with a pre-configured Tendril app manifest (bot user, permissions, and events are already filled in). Click Create, then Install to Workspace.")
            .Muted().Small();
        form |= new Button("Create Slack App").Icon(Icons.Slack).Outline()
            .OnClick(() => client.OpenUrl(SlackAppManifest.BuildInstallUrl()));

        form |= Text.Block("Step 2: Paste the tokens").Bold();
        form |= Text.Block(
                "Bot Token: on the app page under OAuth & Permissions, copy the Bot User OAuth Token (starts with xoxb-). App-Level Token: under Basic Information, App-Level Tokens, generate a token with the connections:write scope (starts with xapp-).")
            .Muted().Small();
        form |= botToken.ToPasswordInput("xoxb-...")
            .WithField().Label("Bot Token");
        form |= appToken.ToPasswordInput("xapp-...")
            .WithField().Label("App-Level Token");

        form |= Text.Block("Step 3: Enable").Bold();
        form |= project.ToSelectInput(projectOptions)
            .WithField().Label("Default Project")
            .Description("Plans created from Slack go to this project. Auto lets the agent pick.");
        form |= enabled.ToBoolInput("Enable Slackbot");

        form |= new Button("Save").Primary()
            .Disabled(!hasChanges || saving.Value || (enabled.Value && tokensMissing))
            .Loading(saving.Value)
            .OnClick(async () =>
            {
                saving.Set(true);
                try
                {
                    if (enabled.Value)
                    {
                        try
                        {
                            var auth = await slackbot.TestConnectionAsync(botToken.Value.Trim(), appToken.Value.Trim());
                            client.Toast($"Connected to {auth.Team} as @{auth.BotName}", "Slack Connection Verified");
                        }
                        catch (Exception ex)
                        {
                            client.Toast(ex.Message, "Slack Connection Failed");
                            return;
                        }
                    }

                    config.Settings.Slackbot = new SlackbotConfig
                    {
                        Enabled = enabled.Value,
                        BotToken = botToken.Value.Trim(),
                        AppToken = appToken.Value.Trim(),
                        Project = project.Value,
                        HistoryLimit = saved?.HistoryLimit ?? 15
                    };
                    config.SaveSettings();
                    client.Toast(enabled.Value ? "Slackbot enabled" : "Slackbot disabled", "Saved");
                }
                finally
                {
                    saving.Set(false);
                }
            });

        return form;
    }
}
