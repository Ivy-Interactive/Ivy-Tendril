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
    public void PolishLinks_DoesNotRedirectMissingPathToSameNamedFile()
    {
        // A same-named file exists elsewhere, but the polisher must NOT silently redirect a
        // missing path onto it — that repointed the wrong file and is left to display-time
        // annotation instead. The link is preserved as authored.
        var subDir = Path.Combine(_repoDir, "src");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "MyFile.cs"), "content");

        var polisher = new MarkdownLinkPolisher();
        var input = "[MyFile.cs](file:///Z:/wrong/path/MyFile.cs)";
        var result = polisher.PolishLinks(input, _plansDir);

        Assert.Equal("[MyFile.cs](file:///Z:/wrong/path/MyFile.cs)", result);
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
        var result = polisher.PolishLinks(input, _plansDir);

        Assert.Contains("file:///Z:/wrong/Dup.cs", result);
    }

    [Fact]
    public void PolishLinks_FixesScreenshotPathToArtifactsFolder()
    {
        var artifactsDir = Path.Combine(_planFolder, "Artifacts");
        Directory.CreateDirectory(artifactsDir);
        File.WriteAllText(Path.Combine(artifactsDir, "screenshot.png"), "img");

        var polisher = new MarkdownLinkPolisher();
        var screenshotPath = Path.Combine(artifactsDir, "screenshot.png").Replace('\\', '/');
        var input = $"[screenshot](file:///{screenshotPath})";
        var result = polisher.PolishLinks(input, _plansDir);

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
        var result = polisher.PolishLinks(input, _plansDir);

        Assert.Equal($"[Test.cs:26](file:///{normalizedPath})", result);
    }

    [Fact]
    public void PolishLinks_RemovesColonLineNumberSuffix()
    {
        var filePath = Path.Combine(_repoDir, "jwt-tester.tsx");
        File.WriteAllText(filePath, "content");
        var normalizedPath = filePath.Replace('\\', '/');

        var polisher = new MarkdownLinkPolisher();
        var input = $"[jwt-tester.tsx:348](file:///{normalizedPath}:348)";
        var result = polisher.PolishLinks(input, _plansDir);

        Assert.Equal($"[jwt-tester.tsx:348](file:///{normalizedPath})", result);
    }

    [Fact]
    public void PolishLinks_RemovesColonLineRangeSuffix()
    {
        var filePath = Path.Combine(_repoDir, "jwt-tester.tsx");
        File.WriteAllText(filePath, "content");
        var normalizedPath = filePath.Replace('\\', '/');

        var polisher = new MarkdownLinkPolisher();
        var input = $"[jwt-tester.tsx:348-350](file:///{normalizedPath}:348-350)";
        var result = polisher.PolishLinks(input, _plansDir);

        Assert.Equal($"[jwt-tester.tsx:348-350](file:///{normalizedPath})", result);
    }

    [Fact]
    public void PolishLinks_PreservesDriveLetterColonWithoutLineNumber()
    {
        var filePath = Path.Combine(_repoDir, "bar.tsx");
        File.WriteAllText(filePath, "content");
        var normalizedPath = filePath.Replace('\\', '/');

        var polisher = new MarkdownLinkPolisher();
        var input = $"[bar.tsx](file:///{normalizedPath})";
        var result = polisher.PolishLinks(input, _plansDir);

        Assert.Equal($"[bar.tsx](file:///{normalizedPath})", result);
    }

    [Fact]
    public void PolishLinks_RemovesBackticksFromLinkText()
    {
        var filePath = Path.Combine(_repoDir, "File.cs");
        File.WriteAllText(filePath, "content");
        var normalizedPath = filePath.Replace('\\', '/');

        var polisher = new MarkdownLinkPolisher();
        var input = $"[`File.cs:131-176`](file:///{normalizedPath})";
        var result = polisher.PolishLinks(input, _plansDir);

        Assert.Equal($"[File.cs:131-176](file:///{normalizedPath})", result);
    }

    [Fact]
    public void PolishLinks_ConvertsBareplanNumbers()
    {
        Directory.CreateDirectory(Path.Combine(_plansDir, "02369-SomePlan"));
        Directory.CreateDirectory(Path.Combine(_plansDir, "03232-OtherPlan"));

        var polisher = new MarkdownLinkPolisher();
        var input = "Plans 02369, 03232";
        var result = polisher.PolishLinks(input, _plansDir);

        Assert.Equal("Plans [02369](plan://02369), [03232](plan://03232)", result);
    }

    [Fact]
    public void PolishLinks_ConvertsPlanRevisionLinks()
    {
        var polisher = new MarkdownLinkPolisher();
        var input = "[Plan 01450](file:///D:/Tendril/Plans/01450-Something/revisions/001.md)";
        var result = polisher.PolishLinks(input, _plansDir);

        Assert.Equal("[Plan 01450](plan://01450)", result);
    }

    [Fact]
    public void PolishLinks_PreservesPlanLinks()
    {
        var polisher = new MarkdownLinkPolisher();
        var input = "[Plan 01234](plan://01234)";
        var result = polisher.PolishLinks(input, _plansDir);

        Assert.Equal("[Plan 01234](plan://01234)", result);
    }

    [Fact]
    public void PolishLinks_PreservesInlineCodeBackticks()
    {
        var polisher = new MarkdownLinkPolisher();
        var input = "Use `SomeMethod()` to call it";
        var result = polisher.PolishLinks(input, _plansDir);

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
        var result = polisher.PolishLinks(input, _plansDir);

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
        var result = polisher.PolishLinks(input, _plansDir);

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
        var result = polisher.PolishLinks(input, _plansDir);

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
        var result = polisher.PolishLinks(input, _plansDir);

        Assert.Equal(input, result);
    }

    [Fact]
    public void PolishLinks_IsIdempotent()
    {
        Directory.CreateDirectory(Path.Combine(_plansDir, "02369-SomePlan"));
        var filePath = Path.Combine(_repoDir, "jwt-tester.tsx");
        File.WriteAllText(filePath, "content");
        var normalizedPath = filePath.Replace('\\', '/');

        var polisher = new MarkdownLinkPolisher();
        var input =
            $"See [`file:///{normalizedPath}:348`](file:///{normalizedPath}:348) and Plans 02369.";

        var once = polisher.PolishLinks(input, _plansDir);
        var twice = polisher.PolishLinks(once, _plansDir);

        Assert.Equal(once, twice);
        Assert.Equal($"See [jwt-tester.tsx:348](file:///{normalizedPath}) and Plans [02369](plan://02369).", once);
    }
}
