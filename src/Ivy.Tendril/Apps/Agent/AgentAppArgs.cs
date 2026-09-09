namespace Ivy.Tendril.Apps.Agent;

/// <summary>
///     Args for <see cref="AgentApp"/>. <paramref name="Title"/> is the fully-formatted tab title
///     (e.g. "#85"), built by the caller; when set, the shell uses it verbatim instead of the
///     branded agent label. <paramref name="SessionId"/> is the terminal's chat session, which the
///     shell assigns when it opens the pane; callers leave it null.
/// </summary>
public record AgentAppArgs(string? Prompt = null, string? Title = null, string? SessionId = null);
