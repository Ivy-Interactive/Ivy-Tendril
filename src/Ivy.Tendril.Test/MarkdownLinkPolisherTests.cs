using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Test;

public class MarkdownLinkPolisherTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly string _planFolder;
    private readonly string _plansDir;
    private readonly string _repoDir;

    public MarkdownLinkPolisherTests()
    {
        _repoDir = Path.Combine(_tempDir.Path, "repo");
        _plansDir = Path.Combine(_tempDir.Path, "Plans");
        _planFolder = Path.Combine(_plansDir, "02369-SomePlan");

        Directory.CreateDirectory(_repoDir);
        Directory.CreateDirectory(_planFolder);
    }

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    [Fact]
    public void PolishLinks_FixesBrokenFileLink_WhenFileExists()
    {
        var subDir = Path.Combine(_repoDir, "src");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "MyFile.cs"), "content");

        var polisher = new MarkdownLinkPolisher();
        var input = "[MyFile.cs](file:///Z:/wrong/path/MyFile.cs)";
        var result = polisher.PolishLinks(input, new[] { _repoDir }, _planFolder);

        var expected = Path.Combine(subDir, "MyFile.cs").Replace('\\', '/');
        Assert.Contains($"file:///{expected}", result);
    }

    [Fact]
    public void PolishLinks_NormalizesPathSegments()
    {
        var result = MarkdownLinkPolisher.NormalizePath("D:\\Repos\\src\\tendril\\..\\tendril\\File.cs");
        Assert.Equal("D:/Repos/src/tendril/File.cs", result);
    }

    [Fact]
    public void PolishLinks_LeavesAmbiguousLinksUnchanged()
    {
        var dir1 = Path.Combine(_repoDir, "dir1");
        var dir2 = Path.Combine(_repoDir, "dir2");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);
        File.WriteAllText(Path.Combine(dir1, "Dup.cs"), "a");
        File.WriteAllText(Path.Combine(dir2, "Dup.cs"), "b");

        var polisher = new MarkdownLinkPolisher();
        var input = "[Dup.cs](file:///Z:/wrong/Dup.cs)";
        var result = polisher.PolishLinks(input, new[] { _repoDir }, _planFolder);

        Assert.Contains("file:///Z:/wrong/Dup.cs", result);
    }

    [Fact]
    public void PolishLinks_FixesScreenshotPathToArtifactsFolder()
    {
        var artifactsDir = Path.Combine(_planFolder, "artifacts");
        Directory.CreateDirectory(artifactsDir);
        File.WriteAllText(Path.Combine(artifactsDir, "screenshot.png"), "img");

        var polisher = new MarkdownLinkPolisher();
        var screenshotPath = Path.Combine(artifactsDir, "screenshot.png").Replace('\\', '/');
        var input = $"[screenshot](file:///{screenshotPath})";
        var result = polisher.PolishLinks(input, new[] { _repoDir }, _planFolder);

        Assert.Contains($"file:///{screenshotPath}", result);
    }

    [Fact]
    public void PolishLinks_RemovesLineNumberAnchors()
    {
        var filePath = Path.Combine(_repoDir, "Test.cs");
        File.WriteAllText(filePath, "content");
        var normalizedPath = filePath.Replace('\\', '/');

        var polisher = new MarkdownLinkPolisher();
        var input = $"[Test.cs:26](file:///{normalizedPath}#26)";
        var result = polisher.PolishLinks(input, new[] { _repoDir }, _planFolder);

        Assert.Equal($"[Test.cs:26](file:///{normalizedPath})", result);
    }

    [Fact]
    public void PolishLinks_RemovesBackticksFromLinkText()
    {
        var filePath = Path.Combine(_repoDir, "File.cs");
        File.WriteAllText(filePath, "content");
        var normalizedPath = filePath.Replace('\\', '/');

        var polisher = new MarkdownLinkPolisher();
        var input = $"[`File.cs:131-176`](file:///{normalizedPath})";
        var result = polisher.PolishLinks(input, new[] { _repoDir }, _planFolder);

        Assert.Equal($"[File.cs:131-176](file:///{normalizedPath})", result);
    }

    [Fact]
    public void PolishLinks_ConvertsBareplanNumbers()
    {
        Directory.CreateDirectory(Path.Combine(_plansDir, "02369-SomePlan"));
        Directory.CreateDirectory(Path.Combine(_plansDir, "03232-OtherPlan"));

        var polisher = new MarkdownLinkPolisher();
        var input = "Plans 02369, 03232";
        var result = polisher.PolishLinks(input, new[] { _repoDir }, _planFolder);

        Assert.Equal("Plans [02369](plan://02369), [03232](plan://03232)", result);
    }

    [Fact]
    public void PolishLinks_ConvertsPlanRevisionLinks()
    {
        var polisher = new MarkdownLinkPolisher();
        var input = "[Plan 01450](file:///D:/Tendril/Plans/01450-Something/revisions/001.md)";
        var result = polisher.PolishLinks(input, new[] { _repoDir }, _planFolder);

        Assert.Equal("[Plan 01450](plan://01450)", result);
    }

    [Fact]
    public void PolishLinks_PreservesPlanLinks()
    {
        var polisher = new MarkdownLinkPolisher();
        var input = "[Plan 01234](plan://01234)";
        var result = polisher.PolishLinks(input, new[] { _repoDir }, _planFolder);

        Assert.Equal("[Plan 01234](plan://01234)", result);
    }

    [Fact]
    public void PolishLinks_PreservesInlineCodeBackticks()
    {
        var polisher = new MarkdownLinkPolisher();
        var input = "Use `SomeMethod()` to call it";
        var result = polisher.PolishLinks(input, new[] { _repoDir }, _planFolder);

        Assert.Equal("Use `SomeMethod()` to call it", result);
    }

    [Fact]
    public void PolishLinks_SimplifiesVerboseLinkTextWithFullPath()
    {
        var subDir = Path.Combine(_repoDir, "src", "Apps");
        Directory.CreateDirectory(subDir);
        var filePath = Path.Combine(subDir, "JobsApp.cs");
        File.WriteAllText(filePath, "content");
        var normalizedPath = filePath.Replace('\\', '/');

        var polisher = new MarkdownLinkPolisher();
        var verboseText = $"file:///{normalizedPath}:205";
        var input = $"[`{verboseText}`](file:///{normalizedPath}#L205)";
        var result = polisher.PolishLinks(input, new[] { _repoDir }, _planFolder);

        Assert.Equal($"[JobsApp.cs:205](file:///{normalizedPath})", result);
    }

    [Fact]
    public void PolishLinks_SimplifiesVerboseLinkTextWithoutLineNumber()
    {
        var subDir = Path.Combine(_repoDir, "src");
        Directory.CreateDirectory(subDir);
        var filePath = Path.Combine(subDir, "Program.cs");
        File.WriteAllText(filePath, "content");
        var normalizedPath = filePath.Replace('\\', '/');

        var polisher = new MarkdownLinkPolisher();
        var verboseText = $"file:///{normalizedPath}";
        var input = $"[`{verboseText}`](file:///{normalizedPath})";
        var result = polisher.PolishLinks(input, new[] { _repoDir }, _planFolder);

        Assert.Equal($"[Program.cs](file:///{normalizedPath})", result);
    }

    [Fact]
    public void PolishLinks_SimplifiesLinkTextWithLineRange()
    {
        var subDir = Path.Combine(_repoDir, "src");
        Directory.CreateDirectory(subDir);
        var filePath = Path.Combine(subDir, "Utils.cs");
        File.WriteAllText(filePath, "content");
        var normalizedPath = filePath.Replace('\\', '/');

        var polisher = new MarkdownLinkPolisher();
        var input = $"[file:///{normalizedPath}:42-50](file:///{normalizedPath})";
        var result = polisher.PolishLinks(input, new[] { _repoDir }, _planFolder);

        Assert.Equal($"[Utils.cs:42-50](file:///{normalizedPath})", result);
    }

    [Fact]
    public void PolishLinks_PreservesAlreadySimplifiedLinks()
    {
        var subDir = Path.Combine(_repoDir, "src", "Apps");
        Directory.CreateDirectory(subDir);
        var filePath = Path.Combine(subDir, "JobsApp.cs");
        File.WriteAllText(filePath, "content");
        var normalizedPath = filePath.Replace('\\', '/');

        var polisher = new MarkdownLinkPolisher();
        var input = $"[JobsApp.cs:205](file:///{normalizedPath})";
        var result = polisher.PolishLinks(input, new[] { _repoDir }, _planFolder);

        Assert.Equal(input, result);
    }
}