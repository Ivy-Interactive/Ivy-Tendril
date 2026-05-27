using Ivy.Tendril.Agents.Helpers;

namespace Ivy.Tendril.Agents.Test.Helpers;

public class BinaryResolverTests
{
    [Fact]
    public void FindOnPath_ExistingBinary_ReturnsPath()
    {
        // 'echo' should exist on all platforms in some form
        var path = BinaryResolver.FindOnPath("echo");
        // On Windows echo might be a shell builtin, so try dotnet
        if (path is null)
        {
            path = BinaryResolver.FindOnPath("dotnet");
        }
        Assert.NotNull(path);
    }

    [Fact]
    public void FindOnPath_NonexistentBinary_ReturnsNull()
    {
        var path = BinaryResolver.FindOnPath("this_binary_definitely_does_not_exist_xyz");
        Assert.Null(path);
    }

    [Fact]
    public void IsInstalled_ExistingBinary_ReturnsTrue()
    {
        Assert.True(BinaryResolver.IsInstalled("dotnet"));
    }

    [Fact]
    public void IsInstalled_NonexistentBinary_ReturnsFalse()
    {
        Assert.False(BinaryResolver.IsInstalled("nonexistent_binary_abc123"));
    }

    [Fact]
    public void FindOnPath_Claude_ReturnsPath()
    {
        var path = BinaryResolver.FindOnPath("claude");
        Assert.NotNull(path);
    }

    [Fact]
    public void FindOnPath_ReturnedPath_FileExists()
    {
        var path = BinaryResolver.FindOnPath("dotnet");
        Assert.NotNull(path);
        Assert.True(File.Exists(path), $"Resolved path should exist: {path}");
    }
}
