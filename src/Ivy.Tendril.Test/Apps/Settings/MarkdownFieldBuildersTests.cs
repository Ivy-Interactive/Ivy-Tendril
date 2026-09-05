using Ivy.Core.Hooks;
using Ivy.Tendril.Apps.Settings;

namespace Ivy.Tendril.Test.Apps.Settings;

public class MarkdownFieldBuildersTests
{
    [Fact]
    public void BuildProjectMemoryContentField_ProducesCodeInputWithMarkdownLanguage()
    {
        var content = new State<string>("# content");

        var field = MarkdownFieldBuilders.BuildProjectMemoryContentField(content);

        Assert.Single(field.Children);
        var codeInput = Assert.IsAssignableFrom<CodeInputBase>(field.Children.Single());
        Assert.Equal(Languages.Markdown, codeInput.Language);
        Assert.Equal("Content (Markdown)", field.Label);
        Assert.True(field.Required);
        Assert.Equal("Markdown memory content (e.g. tech stack, rules, architectural conventions)...", codeInput.Placeholder);
    }

    [Fact]
    public void BuildSkillInstructionsField_ProducesCodeInputWithMarkdownLanguage()
    {
        var instructions = new State<string>("# instructions");

        var field = MarkdownFieldBuilders.BuildSkillInstructionsField(instructions);

        Assert.Single(field.Children);
        var codeInput = Assert.IsAssignableFrom<CodeInputBase>(field.Children.Single());
        Assert.Equal(Languages.Markdown, codeInput.Language);
        Assert.Equal("Inline Instructions", field.Label);
        Assert.False(field.Required);
        Assert.Equal("Instructions / markdown rules...", codeInput.Placeholder);
    }

    [Fact]
    public void BuildProjectContextField_ProducesCodeInputWithMarkdownLanguage()
    {
        var context = new State<string>("# context");

        var field = MarkdownFieldBuilders.BuildProjectContextField(context);

        Assert.Single(field.Children);
        var codeInput = Assert.IsAssignableFrom<CodeInputBase>(field.Children.Single());
        Assert.Equal(Languages.Markdown, codeInput.Language);
        Assert.Equal("Context / Prompt", field.Label);
        Assert.False(field.Required);
        Assert.Equal("Project context or prompt for AI agents...", codeInput.Placeholder);
    }
}
