using System.Text.Json;

namespace Ivy.Tendril.Services.Slack;

public static class SlackAppManifest
{
    public const string AppName = "Tendril";

    public static string BuildManifestJson()
    {
        var manifest = new
        {
            display_information = new
            {
                name = AppName,
                description = "Create Tendril plans by mentioning @Tendril in Slack",
                background_color = "#1f5c3d"
            },
            features = new
            {
                bot_user = new
                {
                    display_name = AppName,
                    always_online = true
                }
            },
            oauth_config = new
            {
                scopes = new
                {
                    bot = new[]
                    {
                        "app_mentions:read",
                        "channels:history",
                        "groups:history",
                        "channels:read",
                        "groups:read",
                        "chat:write",
                        "reactions:write",
                        "users:read"
                    }
                }
            },
            settings = new
            {
                event_subscriptions = new
                {
                    bot_events = new[] { "app_mention" }
                },
                socket_mode_enabled = true,
                org_deploy_enabled = false,
                token_rotation_enabled = false
            }
        };

        return JsonSerializer.Serialize(manifest);
    }

    public static string BuildInstallUrl() =>
        "https://api.slack.com/apps?new_app=1&manifest_json=" + Uri.EscapeDataString(BuildManifestJson());
}
