using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Codex;

namespace Ivy.Tendril.Agents.Test.Codex;

public class CodexHealthCheckTests
{
    private readonly CodexHealthCheck _healthCheck = new();

    [Fact]
    public void AgentId_IsCodex()
    {
        Assert.Equal(AgentId.Codex, _healthCheck.AgentId);
    }

    [Fact]
    public void GetOnboardingInfo_ReturnsCompleteInfo()
    {
        var info = _healthCheck.GetOnboardingInfo();

        Assert.Equal("Codex", info.DisplayName);
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
