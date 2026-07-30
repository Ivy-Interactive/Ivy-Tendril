using Ivy.Tendril.Helpers;
using Xunit;

namespace Ivy.Tendril.Test;

public class PlatformHelperTests
{
    [Theory]
    [InlineData(@"Test-Path \Worktrees\Ivy-Tendril\src\Ivy.Tendril\", @"Test-Path Worktrees\Ivy-Tendril\src\Ivy.Tendril\")]
    [InlineData(@"Test-Path ""\Worktrees\Ivy-Tendril\""", @"Test-Path ""Worktrees\Ivy-Tendril\""")]
    [InlineData(@"Test-Path '/Worktrees/Ivy-Tendril/'", @"Test-Path 'Worktrees/Ivy-Tendril/'")]
    [InlineData(@"Test-Path \artifacts\sample\", @"Test-Path artifacts\sample\")]
    [InlineData(@"Test-Path '/artifacts/sample/'", @"Test-Path 'artifacts/sample/'")]
    [InlineData(@"Test-Path Worktrees\Ivy-Tendril\", @"Test-Path Worktrees\Ivy-Tendril\")]
    [InlineData(@"Test-Path C:\Worktrees\Ivy-Tendril\", @"Test-Path C:\Worktrees\Ivy-Tendril\")]
    [InlineData(@"Test-Path /Users/pavel/git/ivy/Worktrees/Ivy-Tendril", @"Test-Path /Users/pavel/git/ivy/Worktrees/Ivy-Tendril")]
    [InlineData(@"Test-Path ""/Users/pavel/.tendril/Plans/00001/Worktrees/foo""", @"Test-Path ""/Users/pavel/.tendril/Plans/00001/Worktrees/foo""")]
    [InlineData(@"Test-Path /home/user/Worktrees/Ivy-Tendril", @"Test-Path /home/user/Worktrees/Ivy-Tendril")]
    [InlineData(@"Test-Path ~/Worktrees/Ivy-Tendril", @"Test-Path ~/Worktrees/Ivy-Tendril")]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void SanitizeConditionPath_StripsLeadingSlashesFromRelativeWorktreePaths_PreservesAbsolutePaths(string? input, string? expected)
    {
        var actual = PlatformHelper.SanitizeConditionPath(input!);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void EvaluatePowerShellCondition_WithLeadingSlashInCondition_EvaluatesRelativeToWorkingDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TendrilPlatformHelperTest_" + Guid.NewGuid().ToString("N"));
        var subDir = Path.Combine(tempDir, "Worktrees", "TestProject");
        Directory.CreateDirectory(subDir);

        try
        {
            var condition = @"Test-Path \Worktrees\TestProject";
            var result = PlatformHelper.EvaluatePowerShellCondition(condition, tempDir);
            Assert.True(result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
