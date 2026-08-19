using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Copilot;

namespace Ivy.Tendril.Agents.Test.Copilot;

public class CopilotHealthCheckTests
{
    private readonly CopilotHealthCheck _healthCheck = new();

    [Fact]
    public void AgentId_IsCopilot()
    {
        Assert.Equal(AgentId.Copilot, _healthCheck.AgentId);
    }

    [Fact]
    public void GetOnboardingInfo_ReturnsCompleteInfo()
    {
        var info = _healthCheck.GetOnboardingInfo();

        Assert.Equal("Copilot", info.DisplayName);
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
