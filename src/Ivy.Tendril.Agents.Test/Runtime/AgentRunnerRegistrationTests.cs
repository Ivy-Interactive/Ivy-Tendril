using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Claude;
using Ivy.Tendril.Agents.Runtime;

namespace Ivy.Tendril.Agents.Test.Runtime;

public class AgentRunnerRegistrationTests
{
    [Fact]
    public void Register_BasicOverload_RegistersCliParserHealthCheck()
    {
        var runner = new AgentRunner();
        runner.Register(new ClaudeCli(), new ClaudeEventParser(), new ClaudeHealthCheck());

        Assert.Contains(AgentId.Claude, runner.RegisteredAgents);
        Assert.NotNull(runner.GetCli(AgentId.Claude));
        Assert.NotNull(runner.GetHealthCheck(AgentId.Claude));
        Assert.NotNull(runner.GetDescriptor(AgentId.Claude));
    }

    [Fact]
    public void Register_FullOverload_RegistersAllComponents()
    {
        var runner = new AgentRunner();
        runner.Register(
            new ClaudeCli(),
            new ClaudeEventParser(),
            new ClaudeHealthCheck(),
            new ClaudeFailureAnalyzer(),
            new ClaudeSessionCostParser(),
            new ClaudePty());

        Assert.NotNull(runner.GetFailureAnalyzer(AgentId.Claude));
        Assert.NotNull(runner.GetCostParser(AgentId.Claude));
        Assert.NotNull(runner.GetPty(AgentId.Claude));
    }

    [Fact]
    public void Register_FullOverload_OptionalParametersNullable()
    {
        var runner = new AgentRunner();
        runner.Register(
            new ClaudeCli(),
            new ClaudeEventParser(),
            new ClaudeHealthCheck(),
            failureAnalyzer: null,
            costParser: null,
            pty: null);

        Assert.Null(runner.GetFailureAnalyzer(AgentId.Claude));
        Assert.Null(runner.GetCostParser(AgentId.Claude));
        Assert.Null(runner.GetPty(AgentId.Claude));
    }

    [Fact]
    public void GetFailureAnalyzer_UnregisteredAgent_ReturnsNull()
    {
        var runner = new AgentRunner();

        Assert.Null(runner.GetFailureAnalyzer("nonexistent"));
    }

    [Fact]
    public void GetCostParser_UnregisteredAgent_ReturnsNull()
    {
        var runner = new AgentRunner();

        Assert.Null(runner.GetCostParser("nonexistent"));
    }

    [Fact]
    public void GetPty_UnregisteredAgent_ReturnsNull()
    {
        var runner = new AgentRunner();

        Assert.Null(runner.GetPty("nonexistent"));
    }

    [Fact]
    public void GetCli_UnregisteredAgent_Throws()
    {
        var runner = new AgentRunner();

        Assert.Throws<ArgumentException>(() => runner.GetCli("nonexistent"));
    }

    [Fact]
    public void GetHealthCheck_UnregisteredAgent_Throws()
    {
        var runner = new AgentRunner();

        Assert.Throws<ArgumentException>(() => runner.GetHealthCheck("nonexistent"));
    }

    [Fact]
    public void GetDescriptor_UnregisteredAgent_Throws()
    {
        var runner = new AgentRunner();

        Assert.Throws<ArgumentException>(() => runner.GetDescriptor("nonexistent"));
    }

    [Fact]
    public void Register_Fluent_ReturnsSelf()
    {
        var runner = new AgentRunner();

        var result = runner.Register(new ClaudeCli(), new ClaudeEventParser(), new ClaudeHealthCheck());

        Assert.Same(runner, result);
    }

    [Fact]
    public void Register_FullOverload_Fluent_ReturnsSelf()
    {
        var runner = new AgentRunner();

        var result = runner.Register(
            new ClaudeCli(),
            new ClaudeEventParser(),
            new ClaudeHealthCheck(),
            new ClaudeFailureAnalyzer(),
            new ClaudeSessionCostParser(),
            new ClaudePty());

        Assert.Same(runner, result);
    }

    [Fact]
    public void RegisteredAgents_ReflectsRegistration()
    {
        var runner = new AgentRunner();

        Assert.Empty(runner.RegisteredAgents);

        runner.Register(new ClaudeCli(), new ClaudeEventParser(), new ClaudeHealthCheck());

        Assert.Single(runner.RegisteredAgents);
        Assert.Equal(AgentId.Claude, runner.RegisteredAgents[0]);
    }
}
