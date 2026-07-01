using Ivy.Plugins;
using Ivy.Plugins.Messaging;

[assembly: IvyPlugin(typeof(Ivy.Plugin.Slack.SlackPlugin))]

namespace Ivy.Plugin.Slack;

public class SlackPlugin : IIvyPlugin<ITendrilPluginContext>
{
    public PluginManifest Manifest { get; } = new()
    {
        Id = "Ivy.Plugin.Slack",
        Title = "Slack",
        Version = new Version(1, 0, 0),
        Icon = PluginIcon.Named("Slack"),
    };

    public PluginConfigurationSchema ConfigurationSchema { get; } = new SchemaBuilder()
        .AddSecret("BotToken", description: "Slack Bot User OAuth Token (starts with xoxb-)", isRequired: true)
        .AddString("DefaultChannel", defaultValue: "general", description: "Default channel ID or name for messages")
        .AddInteger("MaxRetries", defaultValue: 3, description: "Maximum number of retry attempts for failed messages")
        .Build();

    public void Configure(ITendrilPluginContext context)
    {
        var config = new SlackConfig
        {
            BotToken = context.Config.GetValue("BotToken")!,
            DefaultChannel = context.Config.GetValue("DefaultChannel")!,
            MaxRetries = context.Config.GetInt("MaxRetries") ?? 3
        };

        context.RegisterMessagingChannel(new SlackMessagingChannel(config));
    }
}
