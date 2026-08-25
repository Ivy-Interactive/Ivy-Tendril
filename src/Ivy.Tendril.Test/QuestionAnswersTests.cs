using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Plans;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Test;

/// <summary>
///     <see cref="QuestionAnswers" /> is the host-side merge behind
///     <c>DraftMarkdown.OnAnswersChange</c>. It lives in the widgets assembly, but it is tested here
///     because this is where <see cref="QuestionBlockParser" /> and
///     <see cref="QuestionValidationService" /> are — a merge whose output the validator rejects is a
///     bug, and several of these tests assert exactly that round trip.
/// </summary>
public class QuestionAnswersTests
{
    private const string Markdown = """
        # A plan

        Some prose before the block.

        ```questions
        questions:
          # Which way the retry budget goes.
          - id: retry-scope
            title: Should the retry budget be per-request or per-session?
            header: Retry scope
            other: false
            options:
              - title: Per request
                value: per-request
              - title: Per session
                value: per-session
                recommended: true
          - id: launch-channels
            title: Which channels ship first?
            multiple: true
            options:
              - title: Email
                value: email
              - title: Push
                value: push
        ```

        Prose after the block.
        """;

    private static PlanQuestion Question(string markdown, string id) =>
        QuestionBlockParser.Parse(markdown)
            .SelectMany(block => block.Block?.Questions ?? [])
            .Single(question => question.Id == id);

    private static string Apply(string markdown, string id, params string[]? answer) =>
        QuestionAnswers.Apply(markdown, new QuestionAnswer(id, answer));

    // -------------------------------------------------------------------------------------------
    // Writing an answer
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void Apply_AddsScalarAnswerToSingleSelectQuestion()
    {
        var updated = Apply(Markdown, "retry-scope", "per-session");

        Assert.Contains("    answer: per-session", updated);

        var question = Question(updated, "retry-scope");
        Assert.Equal(AnswerState.Answered, question.AnswerState);
        Assert.Equal(["per-session"], question.AnswerValues);
        Assert.False(question.AnswerIsList);
    }

    [Fact]
    public void Apply_WritesListAnswerWhenQuestionIsMultiple()
    {
        var updated = Apply(Markdown, "launch-channels", "email", "push");

        var question = Question(updated, "launch-channels");
        Assert.True(question.AnswerIsList);
        Assert.Equal(["email", "push"], question.AnswerValues);
        Assert.Empty(QuestionValidationService.Validate(updated));
    }

    [Fact]
    public void Apply_WritesListAnswerForASingleMultiSelectEntry()
    {
        // `answer` is a list iff `multiple` is true, so one selection is still a list.
        var updated = Apply(Markdown, "launch-channels", "email");

        Assert.True(Question(updated, "launch-channels").AnswerIsList);
        Assert.Empty(QuestionValidationService.Validate(updated));
    }

    [Fact]
    public void Apply_AppendsAnswerAsTheLastKeyOfItsQuestion()
    {
        var updated = Apply(Markdown, "retry-scope", "per-session");

        // After the nested options, and before the next question.
        var answerAt = updated.IndexOf("answer: per-session", StringComparison.Ordinal);
        Assert.InRange(answerAt, updated.IndexOf("value: per-session", StringComparison.Ordinal),
            updated.IndexOf("id: launch-channels", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_ReplacesAnExistingAnswer()
    {
        var once = Apply(Markdown, "retry-scope", "per-request");
        var twice = Apply(once, "retry-scope", "per-session");

        Assert.Equal(["per-session"], Question(twice, "retry-scope").AnswerValues);
        // Replaced, not appended: exactly one `answer` key, and the options are untouched.
        Assert.Equal(1, twice.Split("answer:").Length - 1);
        Assert.Contains("    value: per-request", twice);
        Assert.Single(QuestionAnswers.Scan(twice));
    }

    [Fact]
    public void Apply_ReplacesAListAnswerWithAShorterOne()
    {
        var both = Apply(Markdown, "launch-channels", "email", "push");
        var one = Apply(both, "launch-channels", "push");

        var question = Question(one, "launch-channels");
        Assert.Equal(["push"], question.AnswerValues);
        Assert.DoesNotContain("- email", one);
        Assert.Contains("Prose after the block.", one);
    }

    [Fact]
    public void Apply_ReplacesAListAnswerWithAScalarWhenTheQuestionIsSingleSelect()
    {
        const string listAnswer = """
            ```questions
            questions:
              - id: name
                title: What should it be called?
                answer:
                  - delivery
            ```
            """;

        var updated = Apply(listAnswer, "name", "dispatch");

        var question = Question(updated, "name");
        Assert.False(question.AnswerIsList);
        Assert.Equal(["dispatch"], question.AnswerValues);
    }

    // -------------------------------------------------------------------------------------------
    // Skipping and clearing
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void Apply_WritesExplicitNullForAnEmptyAnswer()
    {
        var updated = Apply(Markdown, "retry-scope");

        Assert.Contains("answer: null", updated);
        Assert.Equal(AnswerState.Declined, Question(updated, "retry-scope").AnswerState);
    }

    [Fact]
    public void Apply_RemovesTheAnswerKeyWhenTheAnswerIsNull()
    {
        var answered = Apply(Markdown, "retry-scope", "per-session");
        var cleared = QuestionAnswers.Apply(answered, new QuestionAnswer("retry-scope", null));

        Assert.Equal(AnswerState.Unanswered, Question(cleared, "retry-scope").AnswerState);
        Assert.Equal(Markdown, cleared);
    }

    [Fact]
    public void Apply_RemovesAListAnswerWholesale()
    {
        var answered = Apply(Markdown, "launch-channels", "email", "push");
        var cleared = QuestionAnswers.Apply(answered, new QuestionAnswer("launch-channels", null));

        Assert.Equal(Markdown, cleared);
    }

    [Fact]
    public void Apply_ClearingAnUnansweredQuestionIsANoOp()
    {
        Assert.Equal(Markdown, QuestionAnswers.Apply(Markdown, new QuestionAnswer("retry-scope", null)));
    }

    // -------------------------------------------------------------------------------------------
    // What must survive the edit
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void Apply_LeavesEveryOtherByteAlone()
    {
        var updated = Apply(Markdown, "retry-scope", "per-session");

        // Comments, key order, prose and the untouched question all survive.
        Assert.Contains("  # Which way the retry budget goes.", updated);
        Assert.Contains("Some prose before the block.", updated);
        Assert.Contains("Prose after the block.", updated);
        Assert.Contains("        recommended: true", updated);
        Assert.Equal(AnswerState.Unanswered, Question(updated, "launch-channels").AnswerState);
    }

    [Fact]
    public void Apply_TouchesOnlyTheBlockThatOwnsTheQuestion()
    {
        const string twoBlocks = """
            ```questions
            questions:
              - id: first
                title: One?
            ```

            ```questions
            questions:
              - id: second
                title: Two?
            ```
            """;

        var updated = Apply(twoBlocks, "second", "yep");

        var blocks = QuestionAnswers.Scan(updated);
        Assert.Equal(2, blocks.Count);
        Assert.DoesNotContain("answer", blocks[0].Body);
        Assert.Contains("answer", blocks[1].Body);
    }

    [Fact]
    public void Apply_PreservesCrlfLineEndings()
    {
        var crlf = Markdown.Replace("\n", "\r\n");

        var updated = Apply(crlf, "launch-channels", "email", "push");

        Assert.DoesNotContain(updated.Replace("\r\n", ""), "\n");
        Assert.Equal(["email", "push"], Question(updated, "launch-channels").AnswerValues);
    }

    [Fact]
    public void Apply_KeepsTheIndentationOfAnIndentedFence()
    {
        const string indented = """
            - A list item holding a block:

              ```questions
              questions:
                - id: nested
                  title: Indented?
              ```
            """;

        var updated = Apply(indented, "nested", "yes indeed");

        Assert.Contains("    answer: yes indeed", updated);
        Assert.Equal(["yes indeed"], Question(updated, "nested").AnswerValues);
    }

    [Fact]
    public void Apply_IgnoresAQuestionsFenceThatIsOnlyDocumentation()
    {
        // The inner fence is inside a longer one, so it is prose and holds no live question.
        const string documented = """
            ````
            ```questions
            questions:
              - id: example
                title: Not a live question.
            ```
            ````
            """;

        Assert.Empty(QuestionAnswers.Scan(documented));
        Assert.False(QuestionAnswers.TryApply(documented, new QuestionAnswer("example", ["x"]), out var updated));
        Assert.Equal(documented, updated);
    }

    // -------------------------------------------------------------------------------------------
    // Free text
    // -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("A plain sentence")]
    [InlineData("colons: everywhere: here")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("42")]
    [InlineData("3.14")]
    [InlineData("- leading dash")]
    [InlineData("#hashtag")]
    [InlineData("quotes \"and\" 'more'")]
    [InlineData("a backslash \\ and a tab\there")]
    [InlineData("two\nlines")]
    [InlineData("  padded  ")]
    [InlineData("[bracketed], {braced}")]
    public void Apply_RoundTripsArbitraryFreeText(string text)
    {
        const string freeText = """
            ```questions
            questions:
              - id: service-name
                title: What should the service be called?
            ```
            """;

        var updated = Apply(freeText, "service-name", text);

        Assert.Equal([text], Question(updated, "service-name").AnswerValues);
        Assert.Empty(QuestionValidationService.Validate(updated));
    }

    [Fact]
    public void Apply_RoundTripsFreeTextInAList()
    {
        const string freeText = """
            ```questions
            questions:
              - id: channels
                title: Which channels?
                multiple: true
                options:
                  - title: Email
                    value: email
                  - title: Push
                    value: push
            ```
            """;

        var updated = Apply(freeText, "channels", "email", "carrier pigeon: at dawn");

        Assert.Equal(["email", "carrier pigeon: at dawn"], Question(updated, "channels").AnswerValues);
        Assert.Empty(QuestionValidationService.Validate(updated));
    }

    // -------------------------------------------------------------------------------------------
    // Shapes and misses
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void Apply_HandlesAFlowMappingQuestion()
    {
        const string flow = """
            ```questions
            questions:
              - {id: flow-q, title: Written inline?}
            ```
            """;

        var answered = Apply(flow, "flow-q", "it is");
        Assert.Equal(["it is"], Question(answered, "flow-q").AnswerValues);

        var replaced = Apply(answered, "flow-q", "still is");
        Assert.Equal(["still is"], Question(replaced, "flow-q").AnswerValues);

        var cleared = QuestionAnswers.Apply(replaced, new QuestionAnswer("flow-q", null));
        Assert.Equal(flow, cleared);
    }

    [Fact]
    public void Apply_HandlesASequenceAlignedWithItsKey()
    {
        // Legal YAML: a block sequence may sit at its key's own column.
        const string aligned = """
            ```questions
            questions:
              - id: channels
                multiple: true
                answer:
                - email
                title: Which channels?
            ```
            """;

        var updated = Apply(aligned, "channels", "push");

        var question = Question(updated, "channels");
        Assert.Equal(["push"], question.AnswerValues);
        Assert.Contains("title: Which channels?", updated);
    }

    [Fact]
    public void TryApply_ReportsAMissInsteadOfThrowing()
    {
        Assert.False(QuestionAnswers.TryApply(Markdown, new QuestionAnswer("nope", ["x"]), out var updated));
        Assert.Equal(Markdown, updated);
    }

    [Fact]
    public void Apply_ThrowsOnAnUnknownQuestionId()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => Apply(Markdown, "nope", "x"));

        Assert.Contains("nope", ex.Message);
    }

    [Fact]
    public void Apply_SkipsALegacyPlainTextBlock()
    {
        const string mixed = """
            ```questions
            Should we support notification templates? It is not clear yet.
            ```

            ```questions
            questions:
              - id: real
                title: A real one?
            ```
            """;

        var updated = Apply(mixed, "real", "yes");

        Assert.Contains("Should we support notification templates?", updated);
        Assert.Equal(["yes"], Question(updated, "real").AnswerValues);
    }

    // -------------------------------------------------------------------------------------------
    // Reading an index
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void Read_ReturnsEveryQuestionInDocumentOrder()
    {
        var summaries = QuestionAnswers.Read(Markdown);

        Assert.Equal(["retry-scope", "launch-channels"], summaries.Select(s => s.Id));
        Assert.Equal("Which channels ship first?", summaries[1].Title);
        Assert.Equal("Retry scope", summaries[0].Header);
        Assert.Null(summaries[1].Header);
        Assert.All(summaries, s => Assert.Equal(0, s.BlockIndex));
        Assert.All(summaries, s => Assert.False(s.HasAnswer));
    }

    [Fact]
    public void Read_ReportsAnAnswerAndASkipDifferently()
    {
        var answered = Apply(Markdown, "retry-scope", "per-session");
        var skipped = Apply(answered, "launch-channels");

        var summaries = QuestionAnswers.Read(skipped);

        Assert.True(summaries[0].HasAnswer);
        Assert.False(summaries[0].IsSkipped);

        // A skip is still "dealt with", which is what an index cares about.
        Assert.True(summaries[1].HasAnswer);
        Assert.True(summaries[1].IsSkipped);
    }

    [Fact]
    public void Read_NumbersTheBlockEachQuestionCameFrom()
    {
        const string twoBlocks = """
            ```questions
            questions:
              - id: first
                title: One?
            ```

            ```questions
            questions:
              - id: second
                title: Two?
            ```
            """;

        var summaries = QuestionAnswers.Read(twoBlocks);

        Assert.Equal([0, 1], summaries.Select(s => s.BlockIndex));
    }

    [Fact]
    public void Read_SkipsLegacyAndDocumentationBlocks()
    {
        const string mixed = """
            ```questions
            Should we support notification templates? Not clear yet.
            ```

            ````
            ```questions
            questions:
              - id: example
                title: Documentation, not a question.
            ```
            ````

            ```questions
            questions:
              - id: real
                title: A real one?
            ```
            """;

        var summary = Assert.Single(QuestionAnswers.Read(mixed));
        Assert.Equal("real", summary.Id);
    }

    [Fact]
    public void Read_IgnoresAQuestionWithNoId()
    {
        // Without an id there is nothing for the host to address, so it cannot be an index entry.
        const string idless = """
            ```questions
            questions:
              - title: Nameless?
              - id: named
                title: Named?
            ```
            """;

        var summary = Assert.Single(QuestionAnswers.Read(idless));
        Assert.Equal("named", summary.Id);
    }

    [Fact]
    public void Read_OnADocumentWithNoQuestionsIsEmpty()
    {
        Assert.Empty(QuestionAnswers.Read("# Just a plan\n\nNo questions here."));
        Assert.Empty(QuestionAnswers.Read(""));
    }

    [Fact]
    public void Scan_ReportsBodyOffsetsThatSliceBackToTheBody()
    {
        var block = Assert.Single(QuestionAnswers.Scan(Markdown));

        Assert.Equal(0, block.Index);
        Assert.Equal(block.Body, Markdown[block.BodyStart..block.BodyEnd]);
        Assert.StartsWith("questions:", block.Body);
    }
}
