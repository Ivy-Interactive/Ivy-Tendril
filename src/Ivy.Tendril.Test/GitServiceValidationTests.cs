using Ivy.Tendril.Services;
using Xunit;

namespace Ivy.Tendril.Test;

public class GitServiceValidationTests
{
    [Theory]
    [InlineData("abc1234", true)]                    // 7-char short hash
    [InlineData("abc1234567890abcdef1234567890abcdef12345678", true)] // 40-char full hash
    [InlineData("ABC1234", true)]                    // uppercase (git accepts)
    [InlineData("abc123", false)]                    // too short
    [InlineData("g123456", false)]                   // invalid character
    [InlineData("abc123; rm -rf /", false)]          // injection attempt
    [InlineData("--upload-pack=evil", false)]        // git option injection
    [InlineData("abc\n123", false)]                  // newline
    [InlineData("", false)]                          // empty
    [InlineData(null, false)]                        // null
    public void IsValidCommitHash_ValidatesFormat(string? hash, bool expected)
    {
        var result = GitService.IsValidCommitHash(hash);
        Assert.Equal(expected, result);
    }
}
