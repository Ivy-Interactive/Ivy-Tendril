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
    public static Icons IconFor(string? agentId, IConfigService? config = null)
    {
        if (string.IsNullOrWhiteSpace(agentId))
            return DefaultIcon;

        var normalized = AgentProviderFactory.NormalizeAgentName(agentId);
        if (config != null && normalized.Equals("openaiproxy", StringComparison.OrdinalIgnoreCase))
        {
            var ac = config.Settings.CodingAgents.FirstOrDefault(a =>
                AgentProviderFactory.NormalizeAgentName(a.Name).Equals("openaiproxy", StringComparison.OrdinalIgnoreCase));
            if (ac != null && ac.EnvironmentVariables.TryGetValue("ANTHROPIC_BASE_URL", out var url))
            {
                if (url.Contains("api.berget.ai"))
                    return Icons.ChevronUp;
                if (url.Contains("api.anthropic.com"))
                    return Icons.ClaudeCode;
            }
        }

        return normalized switch
        {
            AgentId.Claude => Icons.ClaudeCode,
            AgentId.Copilot => Icons.Copilot,
            AgentId.Codex => Icons.OpenAI,
            AgentId.Gemini => Icons.Gemini,
            AgentId.Antigravity => Icons.Antigravity,
            AgentId.OpenCode => Icons.OpenCode,
            AgentId.Ivy => Icons.IvyCorner,
            AgentId.OpenAiProxy => Icons.OpenAI,
            AgentId.Berget => Icons.ChevronUp,
            _ => DefaultIcon,
        };
    }

    /// <summary>
    /// Returns the configured agent's display name (e.g. "Claude Code") and icon,
    /// falling back gracefully to <see cref="DefaultLabel"/>/<see cref="DefaultIcon"/>
    /// for unknown or unregistered agents.
    /// </summary>
    public static (string Label, Icons Icon) For(string? agentId, IAgentRunner runner, IConfigService? config = null)
    {
        if (config != null && AgentProviderFactory.NormalizeAgentName(agentId).Equals("openaiproxy", StringComparison.OrdinalIgnoreCase))
        {
            var ac = config.Settings.CodingAgents.FirstOrDefault(a =>
                AgentProviderFactory.NormalizeAgentName(a.Name).Equals("openaiproxy", StringComparison.OrdinalIgnoreCase));
            if (ac != null && ac.EnvironmentVariables.TryGetValue("ANTHROPIC_BASE_URL", out var url))
            {
                if (url.Contains("api.berget.ai"))
                {
                    return ("Berget AI", Icons.ChevronUp);
                }
                if (url.Contains("api.anthropic.com"))
                {
                    return ("Anthropic", Icons.ClaudeCode);
                }
            }
        }

        if (AgentProviderFactory.NormalizeAgentName(agentId).Equals("berget", StringComparison.OrdinalIgnoreCase))
        {
            return ("Berget AI", Icons.ChevronUp);
        }

        var icon = IconFor(agentId, config);

        if (string.IsNullOrWhiteSpace(agentId))
            return (DefaultLabel, icon);

        try
        {
            var displayName = runner.GetCli(AgentProviderFactory.NormalizeAgentName(agentId)).DisplayName;
            return (string.IsNullOrWhiteSpace(displayName) ? DefaultLabel : displayName, icon);
        }
        catch
        {
            return (DefaultLabel, icon);
        }
    }
}
