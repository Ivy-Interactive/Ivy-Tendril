namespace Ivy.Tendril.Apps.Chat;

// NewChat opens a blank composer with no session behind it: the session is created when the first
// message is sent, so abandoning the chat leaves nothing in the history.
public record ChatAppArgs(string? Prompt = null, string? SessionId = null, bool NewChat = false);
