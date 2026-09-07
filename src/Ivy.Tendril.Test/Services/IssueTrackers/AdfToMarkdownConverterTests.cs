using Ivy.Tendril.Services.IssueTrackers.Providers.Jira;

namespace Ivy.Tendril.Test.Services.IssueTrackers;

public class AdfToMarkdownConverterTests
{
    [Fact]
    public void Convert_ReturnsEmptyString_WhenInputIsNullOrEmpty()
    {
        Assert.Equal("", AdfToMarkdownConverter.Convert(null));
        Assert.Equal("", AdfToMarkdownConverter.Convert(""));
        Assert.Equal("", AdfToMarkdownConverter.Convert("   "));
    }

    [Fact]
    public void Convert_ReturnsOriginalText_WhenNotJson()
    {
        var text = "This is just regular plain markdown text with **bold**.";
        var result = AdfToMarkdownConverter.Convert(text);
        Assert.Equal(text, result);
    }

    [Fact]
    public void Convert_ParsesSimpleParagraph()
    {
        var json = """
        {
          "version": 1,
          "type": "doc",
          "content": [
            {
              "type": "paragraph",
              "content": [
                {
                  "type": "text",
                  "text": "Hello world"
                }
              ]
            }
          ]
        }
        """;

        var result = AdfToMarkdownConverter.Convert(json);
        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void Convert_ParsesMarks_BoldItalicCodeLink()
    {
        var json = """
        {
          "version": 1,
          "type": "doc",
          "content": [
            {
              "type": "paragraph",
              "content": [
                {
                  "type": "text",
                  "text": "bold text",
                  "marks": [{"type": "strong"}]
                },
                {
                  "type": "text",
                  "text": " and "
                },
                {
                  "type": "text",
                  "text": "italic text",
                  "marks": [{"type": "em"}]
                },
                {
                  "type": "text",
                  "text": " and "
                },
                {
                  "type": "text",
                  "text": "code block",
                  "marks": [{"type": "code"}]
                },
                {
                  "type": "text",
                  "text": " and "
                },
                {
                  "type": "text",
                  "text": "link text",
                  "marks": [{"type": "link", "attrs": {"href": "https://example.com"}}]
                }
              ]
            }
          ]
        }
        """;

        var result = AdfToMarkdownConverter.Convert(json);
        Assert.Equal("**bold text** and *italic text* and `code block` and [link text](https://example.com)", result);
    }

    [Fact]
    public void Convert_ParsesHeading()
    {
        var json = """
        {
          "version": 1,
          "type": "doc",
          "content": [
            {
              "type": "heading",
              "attrs": {"level": 2},
              "content": [
                {
                  "type": "text",
                  "text": "Section Title"
                }
              ]
            }
          ]
        }
        """;

        var result = AdfToMarkdownConverter.Convert(json);
        Assert.Equal("## Section Title", result);
    }

    [Fact]
    public void Convert_ParsesBulletAndOrderedLists()
    {
        var json = """
        {
          "version": 1,
          "type": "doc",
          "content": [
            {
              "type": "bulletList",
              "content": [
                {
                  "type": "listItem",
                  "content": [
                    {
                      "type": "paragraph",
                      "content": [{"type": "text", "text": "Item A"}]
                    }
                  ]
                },
                {
                  "type": "listItem",
                  "content": [
                    {
                      "type": "paragraph",
                      "content": [{"type": "text", "text": "Item B"}]
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

        var result = AdfToMarkdownConverter.Convert(json);
        Assert.Contains("- Item A", result);
        Assert.Contains("- Item B", result);
    }

    [Fact]
    public void Convert_ParsesCodeBlock()
    {
        var json = """
        {
          "version": 1,
          "type": "doc",
          "content": [
            {
              "type": "codeBlock",
              "attrs": {"language": "csharp"},
              "content": [
                {
                  "type": "text",
                  "text": "var x = 42;"
                }
              ]
            }
          ]
        }
        """;

        var result = AdfToMarkdownConverter.Convert(json);
        Assert.Contains("```csharp", result);
        Assert.Contains("var x = 42;", result);
        Assert.Contains("```", result);
    }
}
