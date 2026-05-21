using Ivy.Plugins;
using Ivy.Plugins.Messaging;

[assembly: IvyPlugin(typeof(Ivy.Plugin.Slack.SlackPlugin))]

namespace Ivy.Plugin.Slack;

public class SlackPlugin : IIvyPlugin
{
    public PluginManifest Manifest { get; } = new()
    {
        Id = "Ivy.Plugin.Slack",
        Name = "Slack",
        ConfigSectionName = "Slack",
        Version = new Version(1, 0, 0),
    };

    public PluginConfigurationSchema ConfigurationSchema { get; } = new()
    {
        Fields =
        [
            new()
            {
                Key = "BotToken",
                Type = ConfigFieldType.Secret,
                IsRequired = true,
                Description = "Slack Bot User OAuth Token (starts with xoxb-)"
            },
            new()
            {
                Key = "DefaultChannel",
                Type = ConfigFieldType.String,
                IsRequired = false,
                Description = "Default channel ID or name for messages",
                DefaultValue = "general"
            },
            new()
            {
                Key = "MaxRetries",
                Type = ConfigFieldType.Integer,
                IsRequired = false,
                Description = "Maximum number of retry attempts for failed messages",
                DefaultValue = "3"
            }
        ]
    };

    public void Configure(IIvyPluginContext context)
    {
        if (context is not ITendrilPluginContext tendrilContext)
            return;

        var config = new SlackConfig
        {
            BotToken = context.Config.GetValue("BotToken")!,
            DefaultChannel = context.Config.GetValue("DefaultChannel")!,
            MaxRetries = int.Parse(context.Config.GetValue("MaxRetries")!)
        };

        tendrilContext.RegisterMessagingChannel(new SlackMessagingChannel(config));
    }
}
