namespace Ivy.Tendril.Apps.Agent;

/// <summary>
///     Args for <see cref="AgentApp"/>. <paramref name="Title"/> is the fully-formatted tab title
///     (e.g. "#85"), built by the caller; when set, the shell uses it verbatim instead of the
///     branded agent label. See TendrilAppShell.ResolveArgsTabTitle and
///     TendrilAppShell.FormatPlanId for the helper callers should use to build it.
/// </summary>
public record AgentAppArgs(string? Prompt = null, string? Title = null);
