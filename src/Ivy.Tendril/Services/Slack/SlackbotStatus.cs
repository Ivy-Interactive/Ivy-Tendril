namespace Ivy.Tendril.Services.Slack;

public enum SlackbotStatus
{
    /// <summary>Bot is not enabled or not configured.</summary>
    Disabled,

    /// <summary>Bot is connecting (or reconnecting) to Slack.</summary>
    Connecting,

    /// <summary>Bot is connected and listening for mentions.</summary>
    Connected,

    /// <summary>Bot stopped due to an unrecoverable error (e.g. invalid tokens).</summary>
    Error
}
