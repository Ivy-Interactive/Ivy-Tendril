using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.OpenAiProxy;

namespace Ivy.Tendril.Agents.Test.OpenAiProxy;

public class OpenAiProxyHealthCheckTests
{
    private readonly OpenAiProxyHealthCheck _healthCheck = new();

    [Fact]
    public void AgentId_IsOpenAiProxy()
    {
        Assert.Equal(AgentId.OpenAiProxy, _healthCheck.AgentId);
    }

    [Fact]
    public void GetOnboardingInfo_ReturnsCompleteInfo()
    {
        var info = _healthCheck.GetOnboardingInfo();

        Assert.Equal("OpenAI Proxy", info.DisplayName);
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
