using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Test;

/// <summary>
///     Math expressions are rendered client-side by KaTeX in the markdown widgets, so the C#
///     side's only job is to carry TeX through to the renderer untouched. These tests pin that
///     down: the markdown pipeline (link polishing plus broken-link annotation) must not mangle
///     TeX delimiters, backslash commands, or braces, and must not mistake a `$$...$$` block for
///     a link or a bare plan number.
/// </summary>
public class MarkdownMathTests : IDisposable
{
    private const string InlineMath = "The relation $$E = mc^2$$ holds.";

    private const string BlockMath = """
        $$
        \sum_{i=1}^{n} i = \frac{n(n+1)}{2}
        $$
        """;

    private readonly string _plansDir;
    private readonly TempDirectoryFixture _tempDir = new();

    public MarkdownMathTests()
    {
        _plansDir = Path.Combine(_tempDir.Path, "Plans");
        Directory.CreateDirectory(_plansDir);
    }

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    [Theory]
    [InlineData(InlineMath)]
    [InlineData(BlockMath)]
    [InlineData(@"$$\varphi = \frac{1 + \sqrt{5}}{2}$$")]
    [InlineData(@"$$A = \begin{pmatrix} a & b \\ c & d \end{pmatrix}$$")]
    public void PolishLinks_PreservesMathExpressions(string markdown)
    {
        var result = new MarkdownLinkPolisher().PolishLinks(markdown, _plansDir);

        Assert.Equal(markdown, result);
    }

    [Theory]
    [InlineData(InlineMath)]
    [InlineData(BlockMath)]
    public void AnnotateAllBrokenLinks_PreservesMathExpressions(string markdown)
    {
        var result = MarkdownHelper.AnnotateAllBrokenLinks(markdown, _plansDir);

        Assert.Equal(markdown, result);
        Assert.DoesNotContain("⚠️", result);
    }

    [Fact]
    public void PolishLinks_PreservesProseDollarSigns()
    {
        // Single dollars are not math (the renderer has singleDollarTextMath disabled), and the
        // C# pipeline must not rewrite them either.
        var markdown = "bash expands any $ (e.g. $env:PORT, or vars like $IsMacOS) so $ survives, costs $5";

        var result = new MarkdownLinkPolisher().PolishLinks(markdown, _plansDir);

        Assert.Equal(markdown, result);
    }

    [Fact]
    public void PolishLinks_PreservesMathAlongsideLinkPolishing()
    {
        // A verbose file link on the same line as math: the link is simplified as usual while
        // the TeX passes through byte for byte.
        var markdown = "See [file:///C:/repo/Foo.cs:42](file:///C:/repo/Foo.cs:42) for $$x^2 + y^2 = z^2$$.";

        var result = new MarkdownLinkPolisher().PolishLinks(markdown, _plansDir);

        Assert.Contains("$$x^2 + y^2 = z^2$$", result);
        Assert.Contains("[Foo.cs:42](file:///C:/repo/Foo.cs)", result);
    }

    [Fact]
    public void PolishLinks_DoesNotTreatFiveDigitExponentAsPlanNumber()
    {
        // Bare five-digit numbers become plan links only after the word "Plan"/"Plans", so a
        // five-digit literal inside math is left alone.
        Directory.CreateDirectory(Path.Combine(_plansDir, "12345-SomePlan"));
        var markdown = @"$$n = 12345$$";

        var result = new MarkdownLinkPolisher().PolishLinks(markdown, _plansDir);

        Assert.Equal(markdown, result);
        Assert.DoesNotContain("plan://", result);
    }

    [Fact]
    public void RevisionWriter_WritesMathExpressionsUnchanged()
    {
        // Revisions are the main math-bearing content, and they are polished on the way to disk.
        var planFolder = Path.Combine(_plansDir, "00026-SupportMathExpressionsInMarkdown");
        Directory.CreateDirectory(planFolder);
        var config = new TestPlanConfigService(_tempDir.Path, tendrilHome: _tempDir.Path);
        var content = $"# Plan\n\n{InlineMath}\n\n{BlockMath}\n";

        var path = RevisionWriter.WriteNext(planFolder, content, config);

        var written = File.ReadAllText(path);
        Assert.Contains("$$E = mc^2$$", written);
        Assert.Contains(@"\sum_{i=1}^{n} i = \frac{n(n+1)}{2}", written);
    }
}
