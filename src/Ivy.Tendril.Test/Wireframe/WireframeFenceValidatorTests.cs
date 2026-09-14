using Ivy.Tendril.Services.Plans;

namespace Ivy.Tendril.Test.Wireframe;

public class WireframeFenceValidatorTests : IDisposable
{
    private readonly string _plan =
        Path.Combine(Path.GetTempPath(), "wireframe-fence-tests", Guid.NewGuid().ToString("N")[..8], "00042-Checkout");

    public WireframeFenceValidatorTests()
    {
        foreach (var name in new[] { "checkout", "summary", "confirm" })
        {
            var src = Path.Combine(_plan, "Wireframes", name, "src");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "main.tsx"), "// scaffolded");
        }
    }

    public void Dispose()
    {
        WireframeTempRoot.Remove(Path.GetDirectoryName(_plan)!);
        GC.SuppressFinalize(this);
    }

    private static string Fence(string body) => $"```wireframe\n{body}\n```\n";

    /// <summary>A revision with its wireframes where they belong: a `## Wireframe` section under the title.</summary>
    private static string Plan(params string[] bodies) =>
        "# Plan\n\n## Wireframe\n\n" + string.Join("\n", bodies.Select(Fence)) + "\n## Problem\n\nText.\n";

    [Fact]
    public void A_well_formed_block_naming_an_existing_wireframe_is_clean()
    {
        var markdown = Plan("name: checkout\nheight: 640\nviewport: Mobile");
        Assert.Empty(WireframeFenceValidator.Validate(markdown, _plan));
    }

    [Fact]
    public void A_plural_section_heading_is_accepted_too()
    {
        var markdown = "# Plan\n\n## Wireframes\n\n" + Fence("checkout") + "\n## Problem\n";
        Assert.Empty(WireframeFenceValidator.Validate(markdown, _plan));
    }

    [Fact]
    public void A_bare_slug_is_shorthand_for_the_name()
    {
        var spec = WireframeFenceValidator.Parse("checkout", out var error);
        Assert.Null(error);
        Assert.Equal(new WireframeFenceSpec("checkout"), spec);
    }

    [Fact]
    public void A_block_naming_a_wireframe_the_plan_does_not_have_is_an_error_that_says_how_to_create_it()
    {
        var issue = Assert.Single(WireframeFenceValidator.Validate(Plan("name: missing"), _plan));

        Assert.Equal(QuestionIssueSeverity.Error, issue.Severity);
        Assert.Equal(5, issue.Line);
        Assert.Contains("no wireframe named 'missing'", issue.Message);
        Assert.Contains("tendril wireframe setup", issue.Message);
    }

    [Theory]
    [InlineData("name: Not A Slug", "lowercase slug")]
    [InlineData("name: checkout\ncolour: red", "unknown key 'colour'")]
    [InlineData("name: checkout\nheight: 12.5", "height")]
    [InlineData("name: checkout\nheight: 0", "height")]
    [InlineData("name: checkout\nviewport: desktop", "viewport")]
    [InlineData("- checkout", "write the block as")]
    [InlineData("", "names no wireframe")]
    [InlineData("name: checkout\ncaption: Converter Page", "unknown key 'caption'")]
    public void Malformed_blocks_are_errors(string body, string message)
    {
        var issue = Assert.Single(WireframeFenceValidator.Validate(Plan(body), planFolder: null));
        Assert.Contains(message, issue.Message);
        Assert.StartsWith("wireframe: ", issue.Message);
    }

    [Fact]
    public void A_third_wireframe_block_is_refused()
    {
        var issue = Assert.Single(WireframeFenceValidator.Validate(Plan("checkout", "summary", "confirm"), _plan));
        Assert.Contains("at most 2", issue.Message);
        Assert.Equal(13, issue.Line);
    }

    [Fact]
    public void A_block_directly_under_the_title_without_its_section_is_refused()
    {
        var markdown = "# Plan\n\n" + Fence("checkout") + "\n## Problem\n";

        var issue = Assert.Single(WireframeFenceValidator.Validate(markdown, _plan));
        Assert.Contains("`## Wireframe` section", issue.Message);
        Assert.Contains("under `# Plan`", issue.Message);
    }

    [Fact]
    public void A_block_in_another_section_is_refused()
    {
        var markdown = "# Plan\n\n## Wireframe\n\n" + Fence("checkout") + "\n## Solution\n\n" + Fence("summary");

        var issue = Assert.Single(WireframeFenceValidator.Validate(markdown, _plan));
        Assert.Contains("under `## Solution`", issue.Message);
    }

    [Fact]
    public void A_wireframe_section_that_is_not_the_first_section_is_refused()
    {
        var markdown = "# Plan\n\n## Problem\n\nText.\n\n## Wireframe\n\n" + Fence("checkout");

        var issue = Assert.Single(WireframeFenceValidator.Validate(markdown, _plan));
        Assert.Contains("must be the first section", issue.Message);
        Assert.Contains("before `## Problem`", issue.Message);
        Assert.Equal(7, issue.Line);
    }

    [Fact]
    public void A_revision_without_wireframes_needs_no_section()
    {
        Assert.Empty(WireframeFenceValidator.Validate("# Plan\n\n## Problem\n\nText.\n", _plan));
    }

    [Fact]
    public void Headings_inside_code_samples_do_not_count_as_sections()
    {
        var markdown = "# Plan\n\n## Wireframe\n\n```markdown\n## Solution\n```\n\n" + Fence("checkout") + "\n## Problem\n";
        Assert.Empty(WireframeFenceValidator.Validate(markdown, _plan));
    }

    [Fact]
    public void A_wireframe_fence_documented_inside_a_longer_fence_is_not_checked()
    {
        var markdown = "````markdown\n" + Fence("name: Not A Slug") + "````\n";
        Assert.Empty(WireframeFenceValidator.Validate(markdown, _plan));
    }

    [Fact]
    public void Questions_blocks_are_still_found_after_the_scanner_was_generalised()
    {
        var markdown = Fence("checkout") + "\n```questions\nquestions:\n  - id: a\n    title: A?\n```\n";
        var blocks = Ivy.Tendril.Helpers.QuestionBlockParser.Parse(markdown);
        Assert.Single(blocks);
        Assert.Equal(5, blocks[0].Line);
    }
}
