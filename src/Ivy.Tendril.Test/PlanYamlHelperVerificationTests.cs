using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Test;

public class PlanYamlHelperVerificationTests
{
    [Fact]
    public void ParseVerificationResultFromReport_FrontmatterPass()
    {
        var content = """
            ---
            result: Pass
            date: 2026-04-25T13:46:00Z
            attempts: 1
            ---
            # DotnetBuild

            ## Output

            Build succeeded.
            """;

        Assert.Equal(VerificationStatus.Pass, PlanYamlHelper.ParseVerificationResultFromReport(content));
    }

    [Fact]
    public void ParseVerificationResultFromReport_FrontmatterFail()
    {
        var content = """
            ---
            result: Fail
            date: 2026-04-25T13:46:00Z
            attempts: 3
            ---
            # DotnetBuild

            ## Output

            Build failed with 2 errors.
            """;

        Assert.Equal(VerificationStatus.Fail, PlanYamlHelper.ParseVerificationResultFromReport(content));
    }

    [Fact]
    public void ParseVerificationResultFromReport_FrontmatterSkipped()
    {
        var content = """
            ---
            result: Skipped
            date: 2026-04-25T13:46:00Z
            attempts: 0
            ---
            # DotnetTest
            """;

        Assert.Equal(VerificationStatus.Skipped, PlanYamlHelper.ParseVerificationResultFromReport(content));
    }

    [Fact]
    public void ParseVerificationResultFromReport_LegacyMarkdownFormat()
    {
        var content = """
            # DotnetBuild

            - **Date:** 2026-04-25T13:46:00Z
            - **Result:** Pass
            - **Attempts:** 2

            ## Output

            Build succeeded with 0 warnings and 0 errors.
            """;

        Assert.Equal(VerificationStatus.Pass, PlanYamlHelper.ParseVerificationResultFromReport(content));
    }

    [Fact]
    public void ParseVerificationResultFromReport_LegacyMarkdownFail()
    {
        var content = """
            # DotnetTest

            - **Date:** 2026-04-25T13:46:00Z
            - **Result:** Fail
            - **Attempts:** 3

            ## Output

            3 tests failed.
            """;

        Assert.Equal(VerificationStatus.Fail, PlanYamlHelper.ParseVerificationResultFromReport(content));
    }

    [Fact]
    public void ParseVerificationResultFromReport_InvalidResultReturnsNull()
    {
        var content = """
            ---
            result: Unknown
            ---
            # Test
            """;

        Assert.Null(PlanYamlHelper.ParseVerificationResultFromReport(content));
    }

    [Fact]
    public void ParseVerificationResultFromReport_EmptyContentReturnsNull()
    {
        Assert.Null(PlanYamlHelper.ParseVerificationResultFromReport(""));
        Assert.Null(PlanYamlHelper.ParseVerificationResultFromReport("  "));
    }

    [Fact]
    public void ParseVerificationResultFromReport_NoFrontmatterNoMarkdownReturnsNull()
    {
        var content = "Just some random text without any result markers.";
        Assert.Null(PlanYamlHelper.ParseVerificationResultFromReport(content));
    }

    [Theory]
    [InlineData(@"D:\Plans\03538-DataTableCellActions")]
    [InlineData("/home/user/Plans/03538-DataTableCellActions")]
    public void ExtractPlanIdFromFolder_StandardFolder(string path)
    {
        Assert.Equal("03538", PlanYamlHelper.ExtractPlanIdFromFolder(path));
    }

    [Fact]
    public void ExtractPlanIdFromFolder_FolderNameOnly()
    {
        Assert.Equal("00015", PlanYamlHelper.ExtractPlanIdFromFolder("00015-TestPlan"));
    }

    [Fact]
    public void ExtractSafeTitleFromFolder_StandardFolder()
    {
        Assert.Equal("DataTableCellActions",
            PlanYamlHelper.ExtractSafeTitleFromFolder(@"D:\Plans\03538-DataTableCellActions"));
    }

    [Fact]
    public void ExtractSafeTitleFromFolder_FolderNameOnly()
    {
        Assert.Equal("TestPlan", PlanYamlHelper.ExtractSafeTitleFromFolder("00015-TestPlan"));
    }

    [Fact]
    public void ToSafeTitle_TruncatesLongTitleToMaxLength()
    {
        var longTitle = new string('A', 200);

        var result = PlanYamlHelper.ToSafeTitle(longTitle);

        Assert.Equal(PlanYamlHelper.SafeTitleMaxLength, result.Length);
    }

    [Fact]
    public void ToSafeTitle_ShortTitleIsUntouched()
    {
        var shortTitle = "Fix Login Bug";

        var result = PlanYamlHelper.ToSafeTitle(shortTitle);

        Assert.Equal("FixLoginBug", result);
        Assert.True(result.Length <= PlanYamlHelper.SafeTitleMaxLength);
    }

    [Fact]
    public void ToSafeTitle_ResultIsAlphanumericAfterTruncation()
    {
        var titleWithSpecialChars = new string('A', 15) + "!@#$%^&*()" + new string('B', 15);

        var result = PlanYamlHelper.ToSafeTitle(titleWithSpecialChars);

        Assert.All(result, c => Assert.True(char.IsLetterOrDigit(c)));
        Assert.True(result.Length <= PlanYamlHelper.SafeTitleMaxLength);
    }
}
