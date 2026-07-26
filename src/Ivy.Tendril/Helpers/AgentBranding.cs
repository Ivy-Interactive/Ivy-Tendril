using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Helpers;

/// <summary>
/// Shared display metadata (icon + name) for coding agents, so the sidebar, tabs,
/// dialogs and setup views all brand the configured agent consistently. The icon
/// map lives here as the single source of truth; the display name comes from the
/// agent's CLI via <see cref="IAgentRunner.GetCli"/>.
/// </summary>
public static class AgentBranding
{
    /// <summary>Icon to use when the agent id is unknown or empty.</summary>
    public const Icons DefaultIcon = Icons.Terminal;

    /// <summary>Display name to use when the agent id can't be resolved.</summary>
    public const string DefaultLabel = "Agent";

    /// <summary>Maps a coding agent id to its logo icon, falling back to <see cref="DefaultIcon"/>.</summary>
    public static Icons IconFor(string? agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId))
            return DefaultIcon;

        return AgentProviderFactory.NormalizeAgentName(agentId) switch
        {
            AgentId.Claude => Icons.ClaudeCode,
            AgentId.Copilot => Icons.Copilot,
            AgentId.Codex => Icons.OpenAI,
            AgentId.Gemini => Icons.Gemini,
            AgentId.Antigravity => Icons.Antigravity,
            AgentId.OpenCode => Icons.OpenCode,
            AgentId.Ivy => Icons.IvyCorner,
            _ => DefaultIcon,
        };
    }

    public static (string Label, Icons Icon) For(string? agentId, IAgentRunner runner)
    {
        var icon = IconFor(agentId);
        return ("Chat", icon);
    }
}
