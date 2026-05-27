namespace Ivy.Tendril.Agents.Abstractions;

public interface IAgentCli : IAgentDescriptor
{
    PromptTransport PromptTransport { get; }
    OutputFormat PreferredOutputFormat { get; }
    AgentProcessSpec BuildProcessSpec(AgentLaunchConfig config);
}
