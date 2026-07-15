using System.Text.Json;

namespace Ivy.Tendril.Plugins.Slack;

public static class SlackAppManifest
{
    public static string BuildManifestJson(string appName = "Tendril")
    {
        var manifest = new
        {
            display_information = new
            {
                name = appName,
                description = "Control Ivy Tendril from Slack: create plans, run jobs, get notifications.",
                background_color = "#1a1d21"
            },
            features = new
            {
                bot_user = new { display_name = appName.ToLowerInvariant(), always_online = true },
                slash_commands = new[]
                {
                    new
                    {
                        command = "/tendril",
                        description = "Control Ivy Tendril",
                        usage_hint = "new <description> | plans | run <id> | status <jobId> | help",
                        should_escape = false
                    }
                }
            },
            oauth_config = new
            {
                scopes = new
                {
                    bot = new[]
                    {
                        "chat:write",
                        "commands",
                        "app_mentions:read",
                        "channels:read",
                        "channels:join",
                        "im:history"
                    }
                }
            },
            settings = new
            {
                event_subscriptions = new { bot_events = new[] { "app_mention", "message.im" } },
                interactivity = new { is_enabled = false },
                org_deploy_enabled = false,
                socket_mode_enabled = true,
                token_rotation_enabled = false
            }
        };

        return JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string BuildCreateAppUrl(string manifestJson) =>
        "https://api.slack.com/apps?new_app=1&manifest_json=" + Uri.EscapeDataString(manifestJson);
}
