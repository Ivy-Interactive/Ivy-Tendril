using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Test.Helpers;

public class RepoPathValidatorTests
{
    [Theory]
    [InlineData("git@github.com:owner/repo.git", RepoPathKind.SshUrl)]
    [InlineData("git@gitlab.com:org/repo", RepoPathKind.SshUrl)]
    [InlineData("git@bitbucket.org:team/repo.git", RepoPathKind.SshUrl)]
    [InlineData("git@custom-host.example.com:user/project.git", RepoPathKind.SshUrl)]
    public void Classify_SshUrl_ReturnsCorrect(string input, RepoPathKind expected)
    {
        Assert.Equal(expected, RepoPathValidator.Classify(input));
    }

    [Theory]
    [InlineData("https://github.com/owner/repo.git", RepoPathKind.HttpUrl)]
    [InlineData("https://github.com/owner/repo", RepoPathKind.HttpUrl)]
    [InlineData("http://gitlab.com/org/repo", RepoPathKind.HttpUrl)]
    [InlineData("https://bitbucket.org/team/project.git", RepoPathKind.HttpUrl)]
    public void Classify_HttpUrl_ReturnsCorrect(string input, RepoPathKind expected)
    {
        Assert.Equal(expected, RepoPathValidator.Classify(input));
    }

    [Theory]
    [InlineData("/home/user/repos/myapp", RepoPathKind.LocalPath)]
    [InlineData("~/code/project", RepoPathKind.LocalPath)]
    [InlineData("/var/lib/repos/test", RepoPathKind.LocalPath)]
    public void Classify_LocalPath_ReturnsCorrect(string input, RepoPathKind expected)
    {
        Assert.Equal(expected, RepoPathValidator.Classify(input));
    }

    [Theory]
    [InlineData("", RepoPathKind.Invalid)]
    [InlineData("   ", RepoPathKind.Invalid)]
    [InlineData("relative/path", RepoPathKind.Invalid)]
    [InlineData("just-a-name", RepoPathKind.Invalid)]
    public void Classify_Invalid_ReturnsInvalid(string input, RepoPathKind expected)
    {
        Assert.Equal(expected, RepoPathValidator.Classify(input));
    }

    [Theory]
    [InlineData("git@github.com:owner/repo.git", "repo")]
    [InlineData("git@gitlab.com:org/my-project", "my-project")]
    [InlineData("https://github.com/owner/repo.git", "repo")]
    [InlineData("https://github.com/owner/repo", "repo")]
    [InlineData("http://gitlab.com/org/my-lib.git", "my-lib")]
    [InlineData("/home/user/repos/myapp", "myapp")]
    [InlineData("~/code/project", "project")]
    public void ExtractRepoName_VariousFormats(string input, string expected)
    {
        Assert.Equal(expected, RepoPathValidator.ExtractRepoName(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ExtractRepoName_InvalidInput_ReturnsNull(string? input)
    {
        Assert.Null(RepoPathValidator.ExtractRepoName(input!));
    }

    [Theory]
    [InlineData("git@github.com:owner/repo.git")]
    [InlineData("https://github.com/owner/repo")]
    [InlineData("/home/user/repos/myapp")]
    [InlineData("~/code/project")]
    public void IsValid_ValidInputs_ReturnsTrue(string input)
    {
        Assert.True(RepoPathValidator.IsValid(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative/path")]
    [InlineData("just-a-name")]
    public void IsValid_InvalidInputs_ReturnsFalse(string input)
    {
        Assert.False(RepoPathValidator.IsValid(input));
    }

    [Theory]
    [InlineData("git@github.com:owner/repo.git")]
    [InlineData("git@gitlab.com:org/repo")]
    public void IsSshUrl_ValidSsh_ReturnsTrue(string input)
    {
        Assert.True(RepoPathValidator.IsSshUrl(input));
    }

    [Theory]
    [InlineData("https://github.com/owner/repo")]
    [InlineData("/home/user/repos/myapp")]
    public void IsSshUrl_NonSsh_ReturnsFalse(string input)
    {
        Assert.False(RepoPathValidator.IsSshUrl(input));
    }

    [Theory]
    [InlineData("https://github.com/owner/repo.git")]
    [InlineData("http://gitlab.com/org/repo")]
    public void IsHttpUrl_ValidHttp_ReturnsTrue(string input)
    {
        Assert.True(RepoPathValidator.IsHttpUrl(input));
    }

    [Theory]
    [InlineData("git@github.com:owner/repo.git")]
    [InlineData("/home/user/repos/myapp")]
    public void IsHttpUrl_NonHttp_ReturnsFalse(string input)
    {
        Assert.False(RepoPathValidator.IsHttpUrl(input));
    }

    [Theory]
    [InlineData("/home/user/repos/myapp")]
    [InlineData("~/code/project")]
    public void IsLocalPath_ValidPath_ReturnsTrue(string input)
    {
        Assert.True(RepoPathValidator.IsLocalPath(input));
    }

    [Theory]
    [InlineData("git@github.com:owner/repo.git")]
    [InlineData("https://github.com/owner/repo")]
    [InlineData("relative/path")]
    public void IsLocalPath_NonPath_ReturnsFalse(string input)
    {
        Assert.False(RepoPathValidator.IsLocalPath(input));
    }
}
