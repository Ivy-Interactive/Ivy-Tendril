using Ivy.Tendril.Services;
using Xunit;

namespace Ivy.Tendril.Test;

public class PlanReaderServiceTests
{
    [Theory]
    [InlineData("03314-MyPlan", "MyPlan")]
    [InlineData("12345-SimpleName", "SimpleName")]
    [InlineData("00001-NameWithMultiple-Dashes", "NameWithMultiple-Dashes")]
    [InlineData("99999-Name_With_Underscores", "Name_With_Underscores")]
    public void ExtractSafeTitle_StandardFormat_ReturnsTitle(string folderName, string expectedTitle)
    {
        var result = PlanReaderService.ExtractSafeTitle(folderName);
        Assert.Equal(expectedTitle, result);
    }

    [Theory]
    [InlineData("03314-MyPlan\\", "MyPlan")]
    [InlineData("03314-MyPlan/", "MyPlan")]
    [InlineData("03314-MyPlan\\/", "MyPlan")]
    public void ExtractSafeTitle_WithTrailingSeparators_TrimsAndReturnsTitle(string folderPath, string expectedTitle)
    {
        var result = PlanReaderService.ExtractSafeTitle(folderPath);
        Assert.Equal(expectedTitle, result);
    }

    [Theory]
    [InlineData("12345-Café-Feature", "Café-Feature")]
    [InlineData("12345-日本語-テスト", "日本語-テスト")]
    [InlineData("12345-Ñoño", "Ñoño")]
    [InlineData("12345-Über-Cool-Feature", "Über-Cool-Feature")]
    public void ExtractSafeTitle_WithNonAsciiCharacters_PreservesCharacters(string folderName, string expectedTitle)
    {
        var result = PlanReaderService.ExtractSafeTitle(folderName);
        Assert.Equal(expectedTitle, result);
    }

    [Theory]
    [InlineData("03314-" + "VeryLongTitle" + "123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890")]
    [InlineData("03314-" + "ExtremelyLongTitle" + "0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890")]
    public void ExtractSafeTitle_WithLongFolderName_ReturnsFullTitle(string folderName)
    {
        var expectedTitle = folderName.Substring(6); // Skip "03314-"
        var result = PlanReaderService.ExtractSafeTitle(folderName);
        Assert.Equal(expectedTitle, result);
    }

    [Fact]
    public void ExtractSafeTitle_WithEmptyString_ReturnsUnknown()
    {
        var result = PlanReaderService.ExtractSafeTitle("");
        Assert.Equal("Unknown", result);
    }

    [Fact]
    public void ExtractSafeTitle_WithNullInput_ReturnsUnknown()
    {
        // Path.GetFileName(null) returns an empty string, which won't match the regex
        var result = PlanReaderService.ExtractSafeTitle(null!);
        Assert.Equal("Unknown", result);
    }

    [Theory]
    [InlineData("NoIdPrefix", "Unknown")]
    [InlineData("123-TooShort", "Unknown")]
    [InlineData("12345", "Unknown")]
    [InlineData("12345-", "Unknown")]
    [InlineData("ABCDE-InvalidIdFormat", "Unknown")]
    public void ExtractSafeTitle_WithInvalidFormat_ReturnsUnknown(string folderName, string expectedResult)
    {
        var result = PlanReaderService.ExtractSafeTitle(folderName);
        Assert.Equal(expectedResult, result);
    }

    [Theory]
    [InlineData("C:\\Plans\\03314-MyPlan", "MyPlan")]
    [InlineData("/home/user/plans/03314-MyPlan", "MyPlan")]
    [InlineData("D:\\Repos\\_Ivy\\03314-TestPlan", "TestPlan")]
    public void ExtractSafeTitle_WithFullPath_ExtractsTitleOnly(string fullPath, string expectedTitle)
    {
        var result = PlanReaderService.ExtractSafeTitle(fullPath);
        Assert.Equal(expectedTitle, result);
    }
}
