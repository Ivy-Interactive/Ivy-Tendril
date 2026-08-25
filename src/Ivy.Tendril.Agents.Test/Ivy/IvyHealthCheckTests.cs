using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Ivy;

namespace Ivy.Tendril.Agents.Test.Providers.Ivy;

public class IvyHealthCheckTests
{
    private readonly IvyHealthCheck _healthCheck = new();

    [Fact]
    public void AgentId_IsIvy()
    {
        Assert.Equal(AgentId.Ivy, _healthCheck.AgentId);
    }

    [Fact]
    public void GetOnboardingInfo_ReturnsCompleteInfo()
    {
        var info = _healthCheck.GetOnboardingInfo();

        Assert.Equal("Ivy Agent", info.DisplayName);
        Assert.NotEmpty(info.InstallCommand);
        Assert.NotNull(info.AuthCommand);
        Assert.NotNull(info.DocsUrl);
    }

    [Fact]
    public async Task CheckInstall_ReturnsResultWithoutThrowing()
    {
        var status = await _healthCheck.CheckInstallAsync();

        Assert.NotNull(status);
        if (status.IsInstalled)
        {
            Assert.NotNull(status.BinaryPath);
        }
        else
        {
            Assert.NotNull(status.Error);
        }
    }
}
