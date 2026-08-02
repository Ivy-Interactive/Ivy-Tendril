using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Test.Helpers;

public class GitStartInfoTests
{
    [Fact]
    public void MakeGitStartInfo_PrependsFsmonitorSuppression()
    {
        var psi = GitHelper.MakeGitStartInfo("status --porcelain");

        Assert.StartsWith("-c core.fsmonitor=false --no-optional-locks ", psi.Arguments);
        Assert.EndsWith("status --porcelain", psi.Arguments);
    }

    [Fact]
    public void MakeGitStartInfo_SetsRedirectionAndNoWindow()
    {
        var psi = GitHelper.MakeGitStartInfo("status");

        Assert.True(psi.RedirectStandardOutput);
        Assert.True(psi.RedirectStandardError);
        Assert.True(psi.CreateNoWindow);
        Assert.False(psi.UseShellExecute);
    }

    [Fact]
    public void MakeGitStartInfo_NullWorkingDirectory_FallsBackToTempPath()
    {
        var psi = GitHelper.MakeGitStartInfo("status", null);

        Assert.Equal(Path.GetTempPath(), psi.WorkingDirectory);
    }

    [Fact]
    public void MakeGitStartInfo_EmptyWorkingDirectory_FallsBackToTempPath()
    {
        var psi = GitHelper.MakeGitStartInfo("status", "");

        Assert.Equal(Path.GetTempPath(), psi.WorkingDirectory);
    }

    [Fact]
    public void MakeGitStartInfo_FileNameIsGit()
    {
        var psi = GitHelper.MakeGitStartInfo("status");

        Assert.Equal("git", psi.FileName);
    }
}
