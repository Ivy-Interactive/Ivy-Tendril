using Ivy.Tendril.Agents.Providers.Ivy;
using Ivy.Tendril.Agents.Providers.OpenAiProxy;
using Xunit;

namespace Ivy.Tendril.Agents.Test.Providers.Ivy;

public class IvyBinaryResolverTests
{
    [Fact]
    public void Resolve_ReturnsValidBinaryOrFallback()
    {
        IvyBinaryResolver.ResetCache();
        var resolved = IvyBinaryResolver.Resolve();
        Assert.NotNull(resolved);

        // If ivy-agent is installed, the returned path must exist on disk.
        // If not installed, it returns the fallback command name "ivy-agent".
        if (resolved != "ivy-agent")
        {
            Assert.True(File.Exists(resolved), $"Resolved path should exist on disk: {resolved}");
        }
    }

    [Fact]
    public void Resolve_CachesResult_UntilReset()
    {
        IvyBinaryResolver.ResetCache();
        var first = IvyBinaryResolver.Resolve();
        var second = IvyBinaryResolver.Resolve();
        Assert.Equal(first, second);

        IvyBinaryResolver.ResetCache();
        var third = IvyBinaryResolver.Resolve();
        Assert.Equal(first, third);
    }

    [Fact]
    public async Task IvyHealthCheck_CheckInstallAsync_ReturnsStatus()
    {
        var hc = new IvyHealthCheck();
        var status = await hc.CheckInstallAsync();
        Assert.NotNull(status);
    }

    [Fact]
    public async Task OpenAiProxyHealthCheck_CheckInstallAsync_ReturnsStatus()
    {
        var hc = new OpenAiProxyHealthCheck();
        var status = await hc.CheckInstallAsync();
        Assert.NotNull(status);
    }
}
