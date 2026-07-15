using System.Text.Json;
using Ivy.Tendril.Plugins.Slack;

namespace Ivy.Tendril.Test.Plugins;

public class SlackAppManifestTests
{
    [Fact]
    public void BuildManifestJson_IsValidJsonWithSocketModeAndSlashCommand()
    {
        var json = SlackAppManifest.BuildManifestJson();
        var root = JsonDocument.Parse(json).RootElement;

        Assert.True(root.GetProperty("settings").GetProperty("socket_mode_enabled").GetBoolean());
        Assert.Equal("/tendril", root.GetProperty("features").GetProperty("slash_commands")[0].GetProperty("command").GetString());

        var scopes = root.GetProperty("oauth_config").GetProperty("scopes").GetProperty("bot")
            .EnumerateArray().Select(s => s.GetString()).ToList();
        Assert.Contains("chat:write", scopes);
        Assert.Contains("commands", scopes);
        Assert.Contains("app_mentions:read", scopes);

        var events = root.GetProperty("settings").GetProperty("event_subscriptions").GetProperty("bot_events")
            .EnumerateArray().Select(s => s.GetString()).ToList();
        Assert.Contains("app_mention", events);
    }

    [Fact]
    public void BuildCreateAppUrl_UrlEncodesManifest()
    {
        var url = SlackAppManifest.BuildCreateAppUrl(SlackAppManifest.BuildManifestJson());

        Assert.StartsWith("https://api.slack.com/apps?new_app=1&manifest_json=", url);
        Assert.DoesNotContain("\"", url[url.IndexOf('=')..]);
        Assert.DoesNotContain("\n", url);
    }

    [Fact]
    public void BuildManifestJson_CustomAppName_IsUsed()
    {
        var json = SlackAppManifest.BuildManifestJson("MyBot");
        var root = JsonDocument.Parse(json).RootElement;
        Assert.Equal("MyBot", root.GetProperty("display_information").GetProperty("name").GetString());
    }
}
