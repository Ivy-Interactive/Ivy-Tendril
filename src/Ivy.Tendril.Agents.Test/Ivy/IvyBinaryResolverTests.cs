using Ivy.Tendril.Agents.Providers.Ivy;
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
}
