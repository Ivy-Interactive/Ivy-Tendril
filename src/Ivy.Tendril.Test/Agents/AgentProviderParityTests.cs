using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test.Agents;

public class AgentProviderParityTests
{
    private static IAgentRunner CreateRunner() => TestAgentRunner.Create();

    public static IEnumerable<object[]> AgentIds =>
    [
        ["claude"],
        ["codex"],
        ["antigravity"],
        ["copilot"],
        ["opencode"]
    ];

    // --- Non-empty Id ---

    [Theory]
    [MemberData(nameof(AgentIds))]
    public void AllAgents_HaveNonEmptyId(string agentId)
    {
        var runner = CreateRunner();
        var cli = runner.GetCli(agentId);
        Assert.False(string.IsNullOrWhiteSpace(cli.Id));
    }

    // --- Non-empty DisplayName ---

    [Theory]
    [MemberData(nameof(AgentIds))]
    public void AllAgents_HaveNonEmptyDisplayName(string agentId)
    {
        var runner = CreateRunner();
        var cli = runner.GetCli(agentId);
        Assert.False(string.IsNullOrWhiteSpace(cli.DisplayName));
    }

    // --- Factory registration ---

    [Theory]
    [MemberData(nameof(AgentIds))]
    public void AllAgents_AreRegisteredInRunner(string agentId)
    {
        var runner = CreateRunner();
        var cli = runner.GetCli(agentId);
        Assert.Equal(agentId, cli.Id);
    }

    // --- All agents resolvable via factory ---

    [Theory]
    [MemberData(nameof(AgentIds))]
    public void AllAgents_ResolvableViaFactory(string agentId)
    {
        var runner = CreateRunner();
        var settings = new TendrilSettings
        {
            CodingAgent = agentId,
            Promptwares = new Dictionary<string, PromptwareConfig>(),
            CodingAgents = new List<AgentConfig>()
        };

        var resolution = AgentProviderFactory.Resolve(runner, settings, "Test");

        Assert.Equal(agentId, resolution.AgentId);
    }
}
