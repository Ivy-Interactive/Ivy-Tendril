using Ivy.Plugins;
using Ivy.Tendril.Plugins;
using Ivy.Tendril.Plugins.Slack;
using Microsoft.Extensions.DependencyInjection;

[assembly: IvyPlugin(typeof(SlackPlugin))]

namespace Ivy.Tendril.Plugins.Slack;

public class SlackPlugin : IIvyPlugin
{
    public const string PluginId = "Ivy.Tendril.Plugin.Slack";

    public PluginManifest Manifest { get; } = new()
    {
        Id = PluginId,
        Title = "Slack Bot",
        Version = typeof(SlackPlugin).Assembly.GetName().Version ?? new Version(1, 0, 0),
        Icon = PluginIcon.Named("MessageCircle")
    };

    public PluginConfigurationSchema? ConfigurationSchema { get; } = new SchemaBuilder()
        .AddSecret(SlackChannelSettings.Keys.BotToken,
            description: "Bot User OAuth Token (xoxb-…)", isRequired: true)
        .AddSecret(SlackChannelSettings.Keys.AppToken,
            description: "App-Level Token with connections:write scope (xapp-…)", isRequired: true)
        .AddString(SlackChannelSettings.Keys.DefaultChannel,
            description: "Channel ID where job notifications are posted (e.g. C0123456789)")
        .AddString(SlackChannelSettings.Keys.AllowedUsers,
            description: "Comma-separated Slack user IDs allowed to run commands (empty = everyone)")
        .Build();

    public object? BuildConfigurationView(IIvyPluginConfig configWriter) =>
        new SlackSetupWizardView(configWriter);

    public void Configure(IIvyPluginContext context)
    {
        var settings = SlackChannelSettings.FromConfig(context.Config);
        context.Services.AddSingleton<ITendrilMessagingChannel>(new SlackMessagingChannel(settings));
    }
}
