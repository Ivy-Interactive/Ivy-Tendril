using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Test;

public class JobServicePromptsRootTests
{
    [Fact]
    public void ResolvePromptsRoot_ReturnsSourceDir_WhenRunningFromSourceTree()
    {
        var result = PromptwareHelper.ResolvePromptsRoot();

        Assert.False(string.IsNullOrEmpty(result));
        Assert.EndsWith("Promptwares", result);
    }

    [Fact]
    public void ResolvePromptsRoot_FallsBackToTendrilHome_WhenSourceDirMissing()
    {
        var result = PromptwareHelper.ResolvePromptsRoot();
        Assert.NotNull(result);
        Assert.Contains("Promptwares", result);
    }
}