using Ivy.Plugins;

namespace Ivy.Tendril.Plugins.Slack;

public record SlackChannelSettings(
    string BotToken,
    string AppToken,
    string? DefaultChannel,
    IReadOnlySet<string> AllowedUsers)
{
    public static class Keys
    {
        public const string BotToken = "BotToken";
        public const string AppToken = "AppToken";
        public const string DefaultChannel = "DefaultChannel";
        public const string AllowedUsers = "AllowedUsers";
    }

    public static SlackChannelSettings FromConfig(IIvyPluginConfig config) => new(
        config.GetValue(Keys.BotToken) ?? "",
        config.GetValue(Keys.AppToken) ?? "",
        config.GetValue(Keys.DefaultChannel),
        ParseAllowedUsers(config.GetValue(Keys.AllowedUsers)));

    public static IReadOnlySet<string> ParseAllowedUsers(string? value) =>
        (value ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public bool IsUserAllowed(string userId) =>
        AllowedUsers.Count == 0 || AllowedUsers.Contains(userId);
}
