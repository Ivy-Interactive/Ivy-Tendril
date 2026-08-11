using System.Text.Json.Nodes;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Slack;

namespace Ivy.Tendril.Test;

public class SlackTextFormatterTests
{
    [Fact]
    public void StripMention_RemovesBotMention()
    {
        var result = SlackTextFormatter.StripMention("<@U0BOT> fix the login bug", "U0BOT");

        Assert.Equal("fix the login bug", result);
    }

    [Fact]
    public void StripMention_RemovesMidSentenceMention()
    {
        var result = SlackTextFormatter.StripMention("hey <@U0BOT> please fix this", "U0BOT");

        Assert.Equal("hey please fix this", result);
    }

    [Fact]
    public void StripMention_PreservesNewlines()
    {
        var result = SlackTextFormatter.StripMention("<@U0BOT> fix this:\nline one\nline two", "U0BOT");

        Assert.Equal("fix this:\nline one\nline two", result);
    }

    [Fact]
    public void StripMention_LeavesOtherMentionsIntact()
    {
        var result = SlackTextFormatter.StripMention("<@U0BOT> ask <@U123> about it", "U0BOT");

        Assert.Equal("ask <@U123> about it", result);
    }

    [Fact]
    public void StripMention_EmptyAfterMention_ReturnsEmpty()
    {
        var result = SlackTextFormatter.StripMention("<@U0BOT>", "U0BOT");

        Assert.Equal("", result);
    }

    [Fact]
    public void Normalize_ReplacesUserMentions()
    {
        var result = SlackTextFormatter.Normalize("ask <@U123> about it", _ => "alice");

        Assert.Equal("ask @alice about it", result);
    }

    [Fact]
    public void Normalize_ReplacesLabeledLinks()
    {
        var result = SlackTextFormatter.Normalize("see <https://example.com/a|the docs>", _ => "");

        Assert.Equal("see the docs (https://example.com/a)", result);
    }

    [Fact]
    public void Normalize_ReplacesPlainLinks()
    {
        var result = SlackTextFormatter.Normalize("see <https://example.com/a>", _ => "");

        Assert.Equal("see https://example.com/a", result);
    }

    [Fact]
    public void Normalize_ReplacesChannelMentions()
    {
        var result = SlackTextFormatter.Normalize("posted in <#C123|general>", _ => "");

        Assert.Equal("posted in #general", result);
    }

    [Fact]
    public void Normalize_DecodesHtmlEntities()
    {
        var result = SlackTextFormatter.Normalize("if a &lt; b &amp;&amp; b &gt; c", _ => "");

        Assert.Equal("if a < b && b > c", result);
    }

    [Fact]
    public void ExtractUserMentions_ReturnsDistinctIds()
    {
        var result = SlackTextFormatter.ExtractUserMentions("<@U1> and <@U2> and <@U1> again");

        Assert.Equal(new[] { "U1", "U2" }, result);
    }

    [Fact]
    public void BuildPlanDescription_IncludesInstructionRequesterAndContext()
    {
        var context = new List<(string Author, string Text)>
        {
            ("alice", "the login test fails on CI"),
            ("bob", "only on chromium though")
        };

        var result = SlackTextFormatter.BuildPlanDescription(
            "fix the flaky login test", "joel", "engineering", context);

        Assert.StartsWith("fix the flaky login test", result);
        Assert.Contains("Requested by @joel via Slack in #engineering.", result);
        Assert.Contains("- @alice: the login test fails on CI", result);
        Assert.Contains("- @bob: only on chromium though", result);
    }

    [Fact]
    public void BuildPlanDescription_OmitsContextSectionWhenEmpty()
    {
        var result = SlackTextFormatter.BuildPlanDescription(
            "fix the bug", "joel", "general", []);

        Assert.DoesNotContain("conversation context", result);
    }

    [Fact]
    public void BuildPlanDescription_FlattensMultilineContextMessages()
    {
        var context = new List<(string Author, string Text)> { ("alice", "line one\nline two") };

        var result = SlackTextFormatter.BuildPlanDescription("task", "joel", "general", context);

        Assert.Contains("- @alice: line one line two", result);
    }
}

public class SlackAppManifestTests
{
    [Fact]
    public void BuildManifestJson_IsValidJsonWithExpectedSettings()
    {
        var json = JsonNode.Parse(SlackAppManifest.BuildManifestJson());

        Assert.NotNull(json);
        Assert.Equal("Tendril", json!["display_information"]?["name"]?.GetValue<string>());
        Assert.True(json["settings"]?["socket_mode_enabled"]?.GetValue<bool>());

        var events = json["settings"]?["event_subscriptions"]?["bot_events"]?.AsArray()
            .Select(e => e?.GetValue<string>()).ToList();
        Assert.Contains("app_mention", events!);

        var scopes = json["oauth_config"]?["scopes"]?["bot"]?.AsArray()
            .Select(s => s?.GetValue<string>()).ToList();
        Assert.Contains("app_mentions:read", scopes!);
        Assert.Contains("channels:history", scopes!);
        Assert.Contains("chat:write", scopes!);
        Assert.Contains("users:read", scopes!);
    }

    [Fact]
    public void BuildInstallUrl_PointsToSlackAppCreation()
    {
        var url = SlackAppManifest.BuildInstallUrl();

        Assert.StartsWith("https://api.slack.com/apps?new_app=1&manifest_json=", url);
        Assert.DoesNotContain("\"", url.Substring("https://api.slack.com/apps?new_app=1&manifest_json=".Length));
    }
}

public class SlackbotConfigTests
{
    [Fact]
    public void LoadSettings_DeserializesSlackbotSection()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tendril-slackbot-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "config.yaml"), @"
slackbot:
  enabled: true
  botToken: xoxb-test
  appToken: xapp-test
  project: Framework
  historyLimit: 25
");

        var service = new ConfigService(new TendrilSettings());

        try
        {
            service.SetTendrilHome(tempDir);

            var slackbot = service.Settings.Slackbot;
            Assert.NotNull(slackbot);
            Assert.True(slackbot!.Enabled);
            Assert.Equal("xoxb-test", slackbot.BotToken);
            Assert.Equal("xapp-test", slackbot.AppToken);
            Assert.Equal("Framework", slackbot.Project);
            Assert.Equal(25, slackbot.HistoryLimit);
        }
        finally
        {
            service.Dispose();
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void SaveSettings_RoundTripsSlackbotSection()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tendril-slackbot-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "config.yaml"), "projects: []\n");

        var service = new ConfigService(new TendrilSettings());

        try
        {
            service.SetTendrilHome(tempDir);

            service.Settings.Slackbot = new SlackbotConfig
            {
                Enabled = true,
                BotToken = "xoxb-abc",
                AppToken = "xapp-def",
                Project = "Auto"
            };
            service.SaveSettings();

            var savedYaml = File.ReadAllText(Path.Combine(tempDir, "config.yaml"));
            Assert.Contains("slackbot:", savedYaml);
            Assert.Contains("xoxb-abc", savedYaml);

            service.ReloadSettings();
            Assert.True(service.Settings.Slackbot?.Enabled);
            Assert.Equal("xoxb-abc", service.Settings.Slackbot?.BotToken);
        }
        finally
        {
            service.Dispose();
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Settings_WithoutSlackbotSection_IsNull()
    {
        var settings = new TendrilSettings();

        Assert.Null(settings.Slackbot);
    }
}
