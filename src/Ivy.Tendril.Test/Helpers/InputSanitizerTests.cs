using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Test.Helpers;

public class InputSanitizerTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("MyProject", "MyProject")]
    [InlineData("my-project_v2.0", "my-project_v2.0")]
    [InlineData("Hello World!", "HelloWorld")]
    [InlineData("project/name", "projectname")]
    [InlineData("café", "caf")]
    [InlineData("a b@c#d$e", "abcde")]
    [InlineData("valid.name-123_ok", "valid.name-123_ok")]
    public void SanitizeProjectName_RemovesInvalidChars(string input, string expected)
    {
        Assert.Equal(expected, InputSanitizer.SanitizeProjectName(input));
    }

    [Fact]
    public void SanitizeProjectName_NullInput_ReturnsEmpty()
    {
        Assert.Equal("", InputSanitizer.SanitizeProjectName(null!));
    }

    [Theory]
    [InlineData("user@example.com", true)]
    [InlineData("name@sub.domain.co", true)]
    [InlineData("a@b.c", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("noatsign.com", false)]
    [InlineData("@missing-local.com", false)]
    [InlineData("missing-domain@", false)]
    [InlineData("missing@tld", false)]
    [InlineData("has spaces@example.com", false)]
    [InlineData("user@has spaces.com", false)]
    public void IsValidEmail_ReturnsExpected(string input, bool expected)
    {
        Assert.Equal(expected, InputSanitizer.IsValidEmail(input));
    }

    [Theory]
    [InlineData("foo/bar", false)]
    [InlineData("foo\\bar", false)]
    [InlineData("my project", false)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("MyProject", true)]
    [InlineData("my-project_v2.0", true)]
    [InlineData("Ivy-Tendril", true)]
    public void IsValidProjectName_ReturnsExpected(string? input, bool expected)
    {
        Assert.Equal(expected, InputSanitizer.IsValidProjectName(input));
    }

    [Fact]
    public void DescribeProjectNameError_InvalidName_ReturnsMessageWithNameAndSuggestion()
    {
        var error = InputSanitizer.DescribeProjectNameError("foo/bar");

        Assert.NotNull(error);
        Assert.Contains("foo/bar", error);
        Assert.Contains("foobar", error);
    }

    [Fact]
    public void DescribeProjectNameError_ValidName_ReturnsNull()
    {
        Assert.Null(InputSanitizer.DescribeProjectNameError("Ivy-Tendril"));
    }
}
