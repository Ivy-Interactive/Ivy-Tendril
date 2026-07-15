using Ivy;
using Ivy.Plugins;
using static Ivy.Layout;
using static Ivy.Text;

namespace Ivy.Tendril.Plugins.Slack;

public class SlackSetupWizardView(IIvyPluginConfig config) : ViewBase
{
    public override object? Build()
    {
        var client = UseService<IClientProvider>();
        var step = UseState(0);
        var botToken = UseState(config.GetValue(SlackChannelSettings.Keys.BotToken) ?? "");
        var appToken = UseState(config.GetValue(SlackChannelSettings.Keys.AppToken) ?? "");
        var defaultChannel = UseState(config.GetValue(SlackChannelSettings.Keys.DefaultChannel) ?? "");
        var allowedUsers = UseState(config.GetValue(SlackChannelSettings.Keys.AllowedUsers) ?? "");
        var channelOptions = UseState<List<SlackChannelInfo>?>((List<SlackChannelInfo>?)null);
        var validationResult = UseState<string?>((string?)null);
        var validationError = UseState<string?>((string?)null);
        var saved = UseState(false);

        var stepper = new Stepper(
            onSelect: e => { step.Set(e.Value); return ValueTask.CompletedTask; },
            selectedIndex: step.Value,
            new StepperItem("create", Icon: Icons.Slack, Label: "Create App"),
            new StepperItem("connect", Icon: Icons.Key, Label: "Connect"),
            new StepperItem("channel", Icon: Icons.Bell, Label: "Notifications"));

        object stepContent = step.Value switch
        {
            0 => BuildCreateAppStep(client, step),
            1 => BuildConnectStep(botToken, appToken, validationResult, validationError, channelOptions, step),
            _ => BuildChannelStep(botToken, appToken, defaultChannel, allowedUsers, channelOptions, saved)
        };

        return Vertical().Gap(5)
            | stepper
            | new Card(content: stepContent)
            | (saved.Value ? Callout.Success("The Slack bot is connected and will start automatically.", title: "Slack bot installed") : null);
    }

    private static object BuildCreateAppStep(IClientProvider client, IState<int> step)
    {
        var manifestJson = SlackAppManifest.BuildManifestJson();
        var createUrl = SlackAppManifest.BuildCreateAppUrl(manifestJson);

        return Vertical().Gap(4)
            | H2("Create your Slack app")
            | Muted("One click creates a pre-configured Slack app — bot user, /tendril command, and permissions included.")
            | (Vertical().Gap(2)
                | P("1. Click the button below — Slack opens with everything pre-filled.")
                | P("2. Pick your workspace and click Create.")
                | P("3. On the app page, click Install to Workspace and approve."))
            | (Horizontal().Gap(2)
                | new Button("Create Slack App", onClick: _ => { client.OpenUrl(createUrl); return ValueTask.CompletedTask; }, icon: Icons.ExternalLink)
                | new Button("Next →", onClick: _ => { step.Set(1); return ValueTask.CompletedTask; }, variant: ButtonVariant.Outline))
            | new Expandable(
                "Manual setup (copy manifest)",
                Vertical().Gap(2)
                    | Muted("Paste this manifest at api.slack.com/apps → Create New App → From a manifest:")
                    | Code(manifestJson));
    }

    private static object BuildConnectStep(
        IState<string> botToken,
        IState<string> appToken,
        IState<string?> validationResult,
        IState<string?> validationError,
        IState<List<SlackChannelInfo>?> channelOptions,
        IState<int> step)
    {
        async ValueTask Validate()
        {
            validationError.Set((string?)null);
            validationResult.Set((string?)null);
            try
            {
                using var botClient = new SlackWebApiClient(botToken.Value.Trim());
                var auth = await botClient.AuthTestAsync();

                using var appClient = new SlackWebApiClient(appToken.Value.Trim());
                await appClient.OpenSocketUrlAsync();

                try
                {
                    channelOptions.Set(await botClient.ListChannelsAsync());
                }
                catch (SlackApiException)
                {
                    channelOptions.Set(new List<SlackChannelInfo>());
                }

                validationResult.Set($"Connected to {auth.TeamName} as @{auth.BotUser}");
                step.Set(2);
            }
            catch (SlackApiException ex)
            {
                validationError.Set(ex.Message);
            }
            catch (Exception ex)
            {
                validationError.Set($"Connection failed: {ex.Message}");
            }
        }

        return Vertical().Gap(4)
            | H2("Connect the bot")
            | Muted("From your new app's page on api.slack.com, copy two tokens:")
            | (Vertical().Gap(2)
                | P("1. OAuth & Permissions → Bot User OAuth Token (starts with xoxb-).")
                | P("2. Basic Information → App-Level Tokens → Generate Token with the connections:write scope (starts with xapp-)."))
            | new Field(botToken.ToTextInput(variant: TextInputVariant.Password, placeholder: "xoxb-..."), label: "Bot User OAuth Token", required: true)
            | new Field(appToken.ToTextInput(variant: TextInputVariant.Password, placeholder: "xapp-..."), label: "App-Level Token", required: true)
            | (validationError.Value is { } error ? Callout.Error(error, title: "Validation failed") : null)
            | (validationResult.Value is { } okText ? Callout.Success(okText, title: "Connected") : null)
            | (Horizontal().Gap(2)
                | new Button("← Back", onClick: _ => { step.Set(0); return ValueTask.CompletedTask; }, variant: ButtonVariant.Outline)
                | new Button("Validate & Continue", onClick: async _ => await Validate(), icon: Icons.Plug));
    }

    private object BuildChannelStep(
        IState<string> botToken,
        IState<string> appToken,
        IState<string> defaultChannel,
        IState<string> allowedUsers,
        IState<List<SlackChannelInfo>?> channelOptions,
        IState<bool> saved)
    {
        var channels = channelOptions.Value;

        IAnyInput channelInput = channels is { Count: > 0 }
            ? defaultChannel.ToSelectInput(
                channels.Select(c => new Option<string>($"#{c.Name}", c.Id)).ToArray(),
                placeholder: "Pick a channel for notifications...")
            : defaultChannel.ToTextInput(placeholder: "C0123456789");

        async ValueTask SaveAsync()
        {
            var channelId = defaultChannel.Value.Trim();
            if (channelId.Length > 0)
            {
                try
                {
                    using var botClient = new SlackWebApiClient(botToken.Value.Trim());
                    await botClient.JoinChannelAsync(channelId);
                }
                catch (Exception)
                {
                }
            }

            config.SetValue(SlackChannelSettings.Keys.BotToken, botToken.Value.Trim());
            config.SetValue(SlackChannelSettings.Keys.AppToken, appToken.Value.Trim());
            if (channelId.Length > 0)
                config.SetValue(SlackChannelSettings.Keys.DefaultChannel, channelId);
            else
                config.RemoveValue(SlackChannelSettings.Keys.DefaultChannel);
            if (allowedUsers.Value.Trim().Length > 0)
                config.SetValue(SlackChannelSettings.Keys.AllowedUsers, allowedUsers.Value.Trim());
            else
                config.RemoveValue(SlackChannelSettings.Keys.AllowedUsers);
            config.Save();
            saved.Set(true);
        }

        return Vertical().Gap(4)
            | H2("Notifications & access")
            | Muted("Choose where job notifications are posted and who may run commands.")
            | new Field(channelInput, label: "Notification channel")
            | new Field(allowedUsers.ToTextInput(placeholder: "U0123ABC, U0456DEF (empty = everyone)"), label: "Allowed Slack user IDs")
            | new Button("Save & Start Bot", onClick: async _ => await SaveAsync(), icon: Icons.Check);
    }
}
