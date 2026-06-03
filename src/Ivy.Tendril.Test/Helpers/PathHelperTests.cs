using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Test.Helpers;

public class PathHelperTests
{
    [Fact]
    public void ResolvePath_TildeOnly_ReturnsUserProfile()
    {
        var expected = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var result = PathHelper.ResolvePath("~");

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResolvePath_TildeSlash_ExpandsToUserProfile()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var expected = Path.GetFullPath(Path.Combine(home, ".tendril"));

        var result = PathHelper.ResolvePath("~/.tendril");

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResolvePath_TildeBackslash_ExpandsToUserProfile()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var expected = Path.GetFullPath(Path.Combine(home, "data"));

        var result = PathHelper.ResolvePath("~\\data");

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResolvePath_DollarVar_ExpandsEnvironmentVariable()
    {
        var varName = "TEST_RESOLVE_PATH_" + Guid.NewGuid().ToString("N")[..8];
        var varValue = Path.Combine(Path.GetTempPath(), "test-resolve");
        Environment.SetEnvironmentVariable(varName, varValue);

        try
        {
            var result = PathHelper.ResolvePath($"${varName}/sub");

            Assert.Equal(Path.GetFullPath(Path.Combine(varValue, "sub")), result);
        }
        finally
        {
            Environment.SetEnvironmentVariable(varName, null);
        }
    }

    [Fact]
    public void ResolvePath_DollarVar_UnsetVariable_ReturnsFullPath()
    {
        var varName = "UNSET_VAR_" + Guid.NewGuid().ToString("N")[..8];
        Environment.SetEnvironmentVariable(varName, null);

        var result = PathHelper.ResolvePath($"${varName}");

        Assert.Equal(Path.GetFullPath($"${varName}"), result);
    }

    [Fact]
    public void ResolvePath_AbsolutePath_ReturnsCanonical()
    {
        var input = OperatingSystem.IsWindows() ? @"C:\Users\test\.tendril" : "/home/test/.tendril";
        var expected = Path.GetFullPath(input);

        var result = PathHelper.ResolvePath(input);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResolvePath_RelativePath_ResolvesAgainstCurrentDir()
    {
        var expected = Path.GetFullPath("relative/path");

        var result = PathHelper.ResolvePath("relative/path");

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResolvePath_PercentEnvVar_ExpandsViaVariableExpansion()
    {
        var varName = "TEST_PCT_" + Guid.NewGuid().ToString("N")[..8];
        var varValue = Path.Combine(Path.GetTempPath(), "pct-test");
        Environment.SetEnvironmentVariable(varName, varValue);

        try
        {
            var result = PathHelper.ResolvePath($"%{varName}%");

            Assert.Equal(Path.GetFullPath(varValue), result);
        }
        finally
        {
            Environment.SetEnvironmentVariable(varName, null);
        }
    }

    [Fact]
    public void AugmentPath_DoesNotThrow()
    {
        var originalPath = Environment.GetEnvironmentVariable("PATH");

        try
        {
            PathHelper.AugmentPath(forceShellPath: false);
            PathHelper.AugmentPath(forceShellPath: true);

            var path = Environment.GetEnvironmentVariable("PATH");
            Assert.NotNull(path);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }
}
