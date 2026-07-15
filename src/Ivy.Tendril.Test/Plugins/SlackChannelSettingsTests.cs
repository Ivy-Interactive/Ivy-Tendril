using Ivy.Tendril.Plugins.Slack;

namespace Ivy.Tendril.Test.Plugins;

public class SlackChannelSettingsTests
{
    [Fact]
    public void ParseAllowedUsers_SplitsAndTrims()
    {
        var users = SlackChannelSettings.ParseAllowedUsers(" U0123 , U0456,U0789 ");
        Assert.Equal(3, users.Count);
        Assert.Contains("U0123", users);
        Assert.Contains("U0789", users);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void IsUserAllowed_NoAllowlist_AllowsEveryone(string? allowedUsers)
    {
        var settings = new SlackChannelSettings("xoxb", "xapp", null, SlackChannelSettings.ParseAllowedUsers(allowedUsers));
        Assert.True(settings.IsUserAllowed("U_ANYONE"));
    }

    [Fact]
    public void IsUserAllowed_WithAllowlist_RestrictsAccess()
    {
        var settings = new SlackChannelSettings("xoxb", "xapp", null, SlackChannelSettings.ParseAllowedUsers("U0123,U0456"));
        Assert.True(settings.IsUserAllowed("U0123"));
        Assert.True(settings.IsUserAllowed("u0123"));
        Assert.False(settings.IsUserAllowed("U_INTRUDER"));
    }
}
