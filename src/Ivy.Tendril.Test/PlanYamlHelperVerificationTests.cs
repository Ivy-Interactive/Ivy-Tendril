using Ivy.Tendril.Helpers;

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

        Assert.Equal("Pass", PlanYamlHelper.ParseVerificationResultFromReport(content));
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

        Assert.Equal("Fail", PlanYamlHelper.ParseVerificationResultFromReport(content));
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

        Assert.Equal("Skipped", PlanYamlHelper.ParseVerificationResultFromReport(content));
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

        Assert.Equal("Pass", PlanYamlHelper.ParseVerificationResultFromReport(content));
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

        Assert.Equal("Fail", PlanYamlHelper.ParseVerificationResultFromReport(content));
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

    [Fact]
    public void ExtractPlanIdFromFolder_StandardFolder()
    {
        Assert.Equal("03538", PlanYamlHelper.ExtractPlanIdFromFolder(@"D:\Plans\03538-DataTableCellActions"));
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
}
