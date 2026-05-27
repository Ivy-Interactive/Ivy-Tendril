using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Test.End2End.Fixtures;

namespace Ivy.Tendril.Agents.Test.End2End.Tests;

[Collection("Agents")]
public class HealthCheckEnd2EndTests(AgentFixture fixture)
{
    [SkippableTheory]
    [InlineData(AgentId.Antigravity)]
    [InlineData(AgentId.Claude)]
    [InlineData(AgentId.Codex)]
    [InlineData(AgentId.Copilot)]
    [InlineData(AgentId.OpenCode)]
    public async Task CheckInstall_ReturnsExpectedResult(string agentId)
    {
        Skip.If(!fixture.IsAvailable(agentId), fixture.SkipReasonIfUnavailable(agentId));

        var hc = fixture.Runner.GetHealthCheck(agentId);
        var status = await hc.CheckInstallAsync();

        Assert.True(status.IsInstalled);
        Assert.NotNull(status.Version);
        Assert.Null(status.Error);
    }

    [SkippableTheory]
    [InlineData(AgentId.Antigravity)]
    [InlineData(AgentId.Claude)]
    [InlineData(AgentId.Codex)]
    [InlineData(AgentId.Copilot)]
    [InlineData(AgentId.OpenCode)]
    public async Task CheckAuth_WhenInstalled_ReturnsStatus(string agentId)
    {
        Skip.If(!fixture.IsAvailable(agentId), fixture.SkipReasonIfUnavailable(agentId));

        var hc = fixture.Runner.GetHealthCheck(agentId);
        var auth = await hc.CheckAuthAsync();

        Assert.Equal(AuthStatus.Authenticated, auth.Status);
    }

    [SkippableTheory]
    [InlineData(AgentId.Antigravity)]
    [InlineData(AgentId.Claude)]
    [InlineData(AgentId.Codex)]
    [InlineData(AgentId.Copilot)]
    [InlineData(AgentId.OpenCode)]
    public async Task GetVersion_WhenInstalled_ReturnsNonNull(string agentId)
    {
        Skip.If(!fixture.IsAvailable(agentId), fixture.SkipReasonIfUnavailable(agentId));

        var hc = fixture.Runner.GetHealthCheck(agentId);
        var version = await hc.GetVersionAsync();

        Assert.NotNull(version);
        Assert.NotEmpty(version);
    }
}
