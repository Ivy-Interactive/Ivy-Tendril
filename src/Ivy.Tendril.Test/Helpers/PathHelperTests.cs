using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Test.Helpers;

[Collection("TendrilHome")]
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

    [Fact]
    public void AugmentPath_PlacesDotnetToolsAfterLocalBin()
    {
        if (OperatingSystem.IsWindows()) return;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dotnetTools = Path.Combine(home, ".dotnet", "tools");
        var localBin = Path.Combine(home, ".local", "bin");

        // Only meaningful when both directories exist on this machine; otherwise AugmentPath
        // never adds the missing one and the ordering assertion below would be vacuous.
        if (!Directory.Exists(dotnetTools) || !Directory.Exists(localBin)) return;

        lock (TestLocks.ConsoleLock)
        {
            var originalPath = Environment.GetEnvironmentVariable("PATH");

            try
            {
                Environment.SetEnvironmentVariable("PATH", string.Empty);

                PathHelper.AugmentPath(forceShellPath: false);

                var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                var dirs = path.Split(':', StringSplitOptions.RemoveEmptyEntries);

                var localBinIndex = Array.IndexOf(dirs, localBin);
                var dotnetToolsIndex = Array.IndexOf(dirs, dotnetTools);

                Assert.True(localBinIndex >= 0, "~/.local/bin should be present in PATH");
                Assert.True(dotnetToolsIndex >= 0, "~/.dotnet/tools should be present in PATH");
                Assert.True(localBinIndex < dotnetToolsIndex, "~/.local/bin should come before ~/.dotnet/tools");
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATH", originalPath);
            }
        }
    }

    [Fact]
    public void EnsureWindowsCliSetup_DoesNotThrow()
    {
        PathHelper.EnsureWindowsCliSetup();
        PathHelper.EnsureCliSymlink();
    }

    [Theory]
    [InlineData("file:///Users/username/file.cs", "/Users/username/file.cs")]
    [InlineData("file:///Users/username/My%20Folder/file.cs", "/Users/username/My Folder/file.cs")]
    [InlineData("file:///Users/username/file.cs#L10", "/Users/username/file.cs")]
    [InlineData("file:///Users/username/file.cs#10", "/Users/username/file.cs")]
    [InlineData("file:///Users/username/file.cs#L10-20", "/Users/username/file.cs")]
    [InlineData("file:///Users/username/file.cs:42", "/Users/username/file.cs")]
    [InlineData("file:///Users/username/file.cs:10-20", "/Users/username/file.cs")]
    [InlineData("file:///C:/project/file.cs", "C:/project/file.cs")]
    [InlineData("file:////C:/project/file.cs", "C:/project/file.cs")]
    [InlineData("file:///C:/My%20Project/file.cs", "C:/My Project/file.cs")]
    [InlineData("file:///C:/project/file.cs#L42", "C:/project/file.cs")]
    [InlineData("file:///C:/project/file.cs:42", "C:/project/file.cs")]
    [InlineData("file://Users/username/file.cs", "/Users/username/file.cs")]
    [InlineData("file://localhost/Users/username/file.cs", "/Users/username/file.cs")]
    [InlineData("FILE:///Users/username/file.cs", "/Users/username/file.cs")]
    [InlineData("FILE:///D:/test/readme.md", "D:/test/readme.md")]
    public void ExtractPathFromFileUri_ValidFileUris_ExtractsExpectedPath(string uri, string expected)
    {
        var result = PathHelper.ExtractPathFromFileUri(uri);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://example.com/page")]
    [InlineData("https://example.com/page")]
    [InlineData("plan://01234")]
    [InlineData("ftp://example.com/file.txt")]
    public void ExtractPathFromFileUri_InvalidOrNonFileUri_ReturnsNull(string? uri)
    {
        var result = PathHelper.ExtractPathFromFileUri(uri);
        Assert.Null(result);
    }
}
