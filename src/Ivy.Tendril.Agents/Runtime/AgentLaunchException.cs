using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Runtime;

public sealed class AgentLaunchException : Exception
{
    public string AgentId { get; }
    public string? BinaryPath { get; }
    public AgentProcessSpec? Spec { get; }

    public AgentLaunchException(string agentId, string message, Exception? inner = null)
        : base(message, inner)
    {
        AgentId = agentId;
    }

    public AgentLaunchException(string agentId, AgentProcessSpec spec, Exception inner)
        : base($"Failed to launch agent '{agentId}': {inner.Message}", inner)
    {
        AgentId = agentId;
        Spec = spec;
        BinaryPath = spec.FileName;
    }
}
