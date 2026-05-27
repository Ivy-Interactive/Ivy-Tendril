using System.Text.Json;
using Ivy.Tendril.Agents.Helpers;

namespace Ivy.Tendril.Agents.Test.Helpers;

public class ContentExtractorTests
{
    [Fact]
    public void ExtractText_PlainString_ReturnsString()
    {
        var element = JsonDocument.Parse("\"hello world\"").RootElement;
        Assert.Equal("hello world", ContentExtractor.ExtractText(element));
    }

    [Fact]
    public void ExtractText_Null_ReturnsNull()
    {
        var element = JsonDocument.Parse("null").RootElement;
        Assert.Null(ContentExtractor.ExtractText(element));
    }

    [Fact]
    public void ExtractText_ArrayOfTextBlocks_ConcatenatesText()
    {
        var json = """[{"type":"text","text":"line 1"},{"type":"text","text":"line 2"}]""";
        var element = JsonDocument.Parse(json).RootElement;
        Assert.Equal("line 1\nline 2", ContentExtractor.ExtractText(element));
    }

    [Fact]
    public void ExtractText_SingleTextBlock_ReturnsSingleString()
    {
        var json = """[{"type":"text","text":"only line"}]""";
        var element = JsonDocument.Parse(json).RootElement;
        Assert.Equal("only line", ContentExtractor.ExtractText(element));
    }

    [Fact]
    public void ExtractText_ArrayOfPlainStrings_ConcatenatesThem()
    {
        var json = """["first","second"]""";
        var element = JsonDocument.Parse(json).RootElement;
        Assert.Equal("first\nsecond", ContentExtractor.ExtractText(element));
    }

    [Fact]
    public void ExtractText_EmptyArray_ReturnsNull()
    {
        var element = JsonDocument.Parse("[]").RootElement;
        Assert.Null(ContentExtractor.ExtractText(element));
    }

    [Fact]
    public void ExtractText_MixedContentBlocks_SkipsNonText()
    {
        var json = """[{"type":"image","source":"x"},{"type":"text","text":"visible"}]""";
        var element = JsonDocument.Parse(json).RootElement;
        Assert.Equal("visible", ContentExtractor.ExtractText(element));
    }

    [Fact]
    public void ExtractText_Number_ReturnsRawText()
    {
        var element = JsonDocument.Parse("42").RootElement;
        Assert.Equal("42", ContentExtractor.ExtractText(element));
    }

    [Fact]
    public void ExtractText_Object_ReturnsRawText()
    {
        var json = """{"key":"value"}""";
        var element = JsonDocument.Parse(json).RootElement;
        Assert.Equal(json, ContentExtractor.ExtractText(element));
    }
}
