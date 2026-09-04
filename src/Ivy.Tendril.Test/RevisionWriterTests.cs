using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Plans;

namespace Ivy.Tendril.Test;

public class RevisionWriterTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly string _planFolder;
    private readonly ConfigService _config;

    public RevisionWriterTests()
    {
        var home = Path.Combine(_tempDir.Path, "home");
        Directory.CreateDirectory(home);
        _planFolder = Path.Combine(home, "Plans", "02369-SomePlan");
        Directory.CreateDirectory(_planFolder);

        _config = new ConfigService(new TendrilSettings(), home);
    }

    public void Dispose() => _tempDir.Dispose();

    [Fact]
    public void WriteNext_PolishesColonLineNumberSuffix_OnDisk()
    {
        var input = "See [jwt-tester.tsx:348](file:///D:/repo/src/jwt-tester.tsx:348).";

        var path = RevisionWriter.WriteNext(_planFolder, input, _config);

        var written = File.ReadAllText(path);
        Assert.Equal("See [jwt-tester.tsx:348](file:///D:/repo/src/jwt-tester.tsx).", written);
        Assert.EndsWith(Path.Combine("Revisions", "001.md"), path);
    }

    [Fact]
    public void WriteNext_IncrementsRevisionNumber()
    {
        var first = RevisionWriter.WriteNext(_planFolder, "first", _config);
        var second = RevisionWriter.WriteNext(_planFolder, "second", _config);

        Assert.EndsWith("001.md", first);
        Assert.EndsWith("002.md", second);
        Assert.Equal("first", File.ReadAllText(first));
        Assert.Equal("second", File.ReadAllText(second));
    }

    [Fact]
    public void WriteNext_LeavesCleanContentUnchanged()
    {
        var clean = "A plain plan with no file links.";

        var path = RevisionWriter.WriteNext(_planFolder, clean, _config);

        Assert.Equal(clean, File.ReadAllText(path));
    }

    [Fact]
    public void WriteNext_RejectsInvalidQuestionBlockAndWritesNothing()
    {
        const string invalid = """
            # A plan

            ```questions
            questions:
              - id: q1
                title: Which one?
                other: false
            ```
            """;

        var ex = Assert.Throws<QuestionValidationException>(
            () => RevisionWriter.WriteNext(_planFolder, invalid, _config));

        Assert.Equal("question 1: other: false with no options is unanswerable", Assert.Single(ex.Issues).Message);
        Assert.Contains("line 3:", ex.Message);

        // Nothing on disk, so the rejected revision did not consume a number.
        var revisions = Path.Combine(_planFolder, "Revisions");
        Assert.True(!Directory.Exists(revisions) || Directory.GetFiles(revisions).Length == 0);
        Assert.EndsWith("001.md", RevisionWriter.WriteNext(_planFolder, "next", _config));
    }

    [Fact]
    public void WriteNext_ReportsEveryErrorAtOnce()
    {
        const string invalid = """
            ```questions
            questions:
              - id: q1
                title: Which one?
                options:
                  - title: First
                    value: jwt
                  - title: Other
                    value: jwt
            ```
            """;

        var ex = Assert.Throws<QuestionValidationException>(
            () => RevisionWriter.WriteNext(_planFolder, invalid, _config));

        Assert.Equal(2, ex.Issues.Count);
        Assert.Contains("duplicate option value 'jwt'", ex.Message);
        Assert.Contains("duplicates what other: true provides", ex.Message);
    }

    [Fact]
    public void WriteNext_SkipsQuestionValidationWhenAsked()
    {
        const string invalid = """
            ```questions
            questions:
              - id: q1
                title: Which one?
                other: false
            ```
            """;

        var path = RevisionWriter.WriteNext(_planFolder, invalid, _config, validateQuestions: false);

        Assert.Equal(invalid, File.ReadAllText(path));
    }

    [Fact]
    public void WriteNext_ReportsLegacyBlocksAsWarningsWithoutBlocking()
    {
        const string legacy = """
            ```questions
            Should this use JWTs or server sessions?
            ```
            """;

        var path = RevisionWriter.WriteNext(_planFolder, legacy, _config, out var warnings);

        Assert.Equal(legacy, File.ReadAllText(path));
        Assert.Equal(QuestionIssueSeverity.Warning, Assert.Single(warnings).Severity);
    }

    [Fact]
    public void WriteNext_LeavesQuestionBlockContentUnpolished()
    {
        // The same bait inside and outside the fence: outside it must be polished, inside it must
        // not, because option values and answers are matched literally.
        const string bait = "See [helpers.cs:348](file:///D:/repo/helpers.cs:348) and Plan 02369.";
        var input = $$"""
            {{bait}}

            ```questions
            questions:
              - id: q1
                title: Which one?
                description: "{{bait}}"
                options:
                  - title: First
                    value: first
                  - title: Second
                    value: second
            ```
            """;

        var written = File.ReadAllText(RevisionWriter.WriteNext(_planFolder, input, _config));

        // Outside the fence: polished (the :348 suffix stripped, the plan number linked).
        Assert.StartsWith(
            "See [helpers.cs:348](file:///D:/repo/helpers.cs) and Plan [02369](plan://02369).",
            written);

        // Inside the fence: byte for byte what the agent wrote.
        Assert.Contains($"    description: \"{bait}\"", written);
        Assert.EndsWith(input[input.IndexOf("```questions", StringComparison.Ordinal)..], written);
    }

    [Fact]
    public void WriteNext_PreservesBlankLinesAroundQuestionBlock()
    {
        // Splitting the document around the fence must not lose empty segments: a document that
        // opens with a blank line makes the first polished segment the empty string.
        const string input = "\n\n```questions\nquestions:\n  - id: q1\n    title: Which one?\n```\n\ntail\n";

        var written = File.ReadAllText(RevisionWriter.WriteNext(_planFolder, input, _config));

        Assert.Equal(input, written);
    }
}
