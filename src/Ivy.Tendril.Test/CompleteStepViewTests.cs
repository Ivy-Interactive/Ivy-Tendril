using Ivy.Tendril.Apps.Onboarding;

namespace Ivy.Tendril.Test;

public class CompleteStepViewTests
{
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
        Assert.Equal(expected, CompleteStepView.IsValidEmail(input));
    }
}
