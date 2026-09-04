using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Test;

public class QuestionBlockParserTests
{
    [Fact]
    public void Parse_ReadsSingleSelectFixedSet()
    {
        const string markdown = """
            # A plan

            ```questions
            questions:
              - id: auth-scheme
                title: Which auth scheme?
                header: Auth
                other: false
                options:
                  - title: JSON Web Tokens
                    value: jwt
                    recommended: true
                  - title: Server sessions
                    description: Stateful, needs a store.
                    value: sessions
            ```
            """;

        var blocks = QuestionBlockParser.Parse(markdown);

        var question = Assert.Single(Assert.Single(blocks).Block!.Questions);
        Assert.Equal("auth-scheme", question.Id);
        Assert.Equal("Which auth scheme?", question.Title);
        Assert.Equal("Auth", question.Header);
        Assert.False(question.Other);
        Assert.False(question.Multiple);
        Assert.Equal(2, question.Options!.Count);
        Assert.Equal("jwt", question.Options[0].Value);
        Assert.True(question.Options[0].Recommended);
        Assert.Equal("Stateful, needs a store.", question.Options[1].Description);
        Assert.False(question.Options[1].Recommended);
    }

    [Fact]
    public void Parse_ReadsMultiSelectOpenSet()
    {
        const string markdown = """
            ```questions
            questions:
              - id: q1
                title: Which platforms ship first?
                multiple: true
                options:
                  - title: Windows
                    value: windows
                  - title: macOS
                    value: macos
                  - title: Linux
                    value: linux
            ```
            """;

        var question = Assert.Single(Assert.Single(QuestionBlockParser.Parse(markdown)).Block!.Questions);

        Assert.True(question.Multiple);
        Assert.True(question.Other); // default when the key is absent
        Assert.Equal(3, question.Options!.Count);
    }

    [Fact]
    public void Parse_ReadsPureFreeTextQuestion()
    {
        const string markdown = """
            ```questions
            questions:
              - id: q1
                title: What should the feature be called?
                description: Anything is fine as long as it is not "Manager".
            ```
            """;

        var question = Assert.Single(Assert.Single(QuestionBlockParser.Parse(markdown)).Block!.Questions);

        Assert.Null(question.Options);
        Assert.True(question.Other);
        Assert.Equal(AnswerState.Unanswered, question.AnswerState);
    }

    [Fact]
    public void Parse_ReportsOneBasedLineOfOpeningFence()
    {
        const string markdown = """
            # A plan

            Some prose.

            ```questions
            questions:
              - id: q1
                title: Really?
            ```
            """;

        Assert.Equal(5, Assert.Single(QuestionBlockParser.Parse(markdown)).Line);
    }

    [Fact]
    public void Parse_IgnoresQuestionsFenceNestedInLongerFence()
    {
        // A plan that documents the format must not fail its own validator.
        const string markdown = """"
            Here is how the format looks:

            ````
            ```questions
            questions:
              - id: q1
                title: An example, not a real question.
            ```
            ````
            """";

        Assert.Empty(QuestionBlockParser.Parse(markdown));
    }

    [Fact]
    public void Parse_ReturnsEveryTopLevelBlockInDocumentOrder()
    {
        const string markdown = """
            # A plan

            ```questions
            questions:
              - id: q1
                title: First scope question?
            ```

            ## Solution

            ```csharp
            var x = 1;
            ```

            ```questions
            questions:
              - id: q1
                title: Second design question?
            ```
            """;

        var blocks = QuestionBlockParser.Parse(markdown);

        Assert.Equal(2, blocks.Count);
        Assert.Equal("First scope question?", blocks[0].Block!.Questions[0].Title);
        Assert.Equal("Second design question?", blocks[1].Block!.Questions[0].Title);
        Assert.True(blocks[0].Line < blocks[1].Line);
    }

    // ------------------------------------------------------------------ wrapper-less shapes
    //
    // A fence that already says `questions` makes the word inside look redundant, so agents keep
    // leaving it out. Read the same, these are answerable; read as legacy, they render as a code
    // listing and the plan stalls on a question nobody can answer.

    [Fact]
    public void Parse_ReadsABareSequenceWrittenWithoutTheQuestionsKey()
    {
        const string markdown = """
            ```questions
            - id: caching-strategy
              title: Which caching strategy should we use?
              options:
                - title: In-Memory
                  value: in-memory
                - title: Distributed Redis
                  value: redis
            - id: eviction
              title: How should entries expire?
            ```
            """;

        var block = Assert.Single(QuestionBlockParser.Parse(markdown));

        Assert.False(block.IsLegacy);
        Assert.Null(block.YamlError);
        Assert.Equal(2, block.Block!.Questions.Count);
        Assert.Equal("caching-strategy", block.Block.Questions[0].Id);
        Assert.Equal("Which caching strategy should we use?", block.Block.Questions[0].Title);
        Assert.Equal("redis", block.Block.Questions[0].Options![1].Value);
        Assert.Equal("eviction", block.Block.Questions[1].Id);
    }

    [Fact]
    public void Parse_ReadsASingleQuestionWrittenWithoutAnyWrapper()
    {
        const string markdown = """
            ```questions
            id: confirmation-prompt
            title: Should we prompt before deleting?
            other: false
            options:
              - title: Yes
                value: prompt
              - title: No
                value: silent
            ```
            """;

        var block = Assert.Single(QuestionBlockParser.Parse(markdown));

        Assert.False(block.IsLegacy);
        var question = Assert.Single(block.Block!.Questions);
        Assert.Equal("confirmation-prompt", question.Id);
        Assert.False(question.Other);
        Assert.Equal(2, question.Options!.Count);
    }

    // The `answer` key is read off the raw YAML, so each shape has to find it where it sits.
    [Fact]
    public void Parse_StampsAnswersOnABareSequence()
    {
        const string markdown = """
            ```questions
            - id: q1
              title: Which way?
              answer: left
            - id: q2
              title: And then?
            ```
            """;

        var questions = Assert.Single(QuestionBlockParser.Parse(markdown)).Block!.Questions;

        Assert.Equal(AnswerState.Answered, questions[0].AnswerState);
        Assert.Equal("left", Assert.Single(questions[0].AnswerValues));
        Assert.Equal(AnswerState.Unanswered, questions[1].AnswerState);
    }

    [Fact]
    public void Parse_StampsAnswersOnASingleQuestionWithoutAWrapper()
    {
        const string markdown = """
            ```questions
            id: q1
            title: Which way?
            answer: left
            ```
            """;

        var question = Assert.Single(Assert.Single(QuestionBlockParser.Parse(markdown)).Block!.Questions);

        Assert.Equal(AnswerState.Answered, question.AnswerState);
        Assert.Equal("left", Assert.Single(question.AnswerValues));
    }

    [Fact]
    public void Parse_TreatsABulletListOfProseAsLegacy()
    {
        // The pre-schema form is often a list. Without an `id` on every item a sequence is prose —
        // and prose must warn, never fail the write.
        const string markdown = """
            ```questions
            - Should this use JWTs or server sessions?
            - How long should a session live?
            ```
            """;

        var block = Assert.Single(QuestionBlockParser.Parse(markdown));

        Assert.True(block.IsLegacy);
        Assert.Null(block.YamlError);
    }

    [Fact]
    public void Parse_TreatsAMappingWithoutAnIdAsLegacy()
    {
        const string markdown = """
            ```questions
            topic: caching
            note: we could not settle this from the code
            ```
            """;

        var block = Assert.Single(QuestionBlockParser.Parse(markdown));

        Assert.True(block.IsLegacy);
        Assert.Null(block.YamlError);
    }

    [Fact]
    public void Parse_ReportsBrokenYamlInAWrapperLessBlockAsError()
    {
        // An opening `- id:` says the body meant to be structured, so what follows it is an error to
        // fix rather than prose to leave alone.
        const string markdown = """
            ```questions
            - id: q1
              title: Broken
              options: [unclosed
            ```
            """;

        var block = Assert.Single(QuestionBlockParser.Parse(markdown));

        Assert.False(block.IsLegacy);
        Assert.NotNull(block.YamlError);
    }

    [Fact]
    public void Parse_RejectsAnUnknownKeyInAWrapperLessBlock()
    {
        // The schema is additionalProperties: false in every shape, not just the canonical one.
        const string markdown = """
            ```questions
            - id: q1
              title: Which way?
              titel: typo
            ```
            """;

        var block = Assert.Single(QuestionBlockParser.Parse(markdown));

        Assert.False(block.IsLegacy);
        Assert.NotNull(block.YamlError);
    }

    [Fact]
    public void Parse_TreatsProseBodyAsLegacyWithoutYamlError()
    {
        const string markdown = """
            ```questions
            Should this use JWTs or server sessions? We could not tell from the code.
            ```
            """;

        var block = Assert.Single(QuestionBlockParser.Parse(markdown));

        Assert.True(block.IsLegacy);
        Assert.Null(block.YamlError);
        Assert.Null(block.Block);
    }

    [Fact]
    public void Parse_TreatsProseThatIsNotValidYamlAsLegacy()
    {
        // Prose with stray colons does not parse as YAML, and must still never be an error.
        const string markdown = """
            ```questions
            Open: should we do this? Note: the caller decides: here or there.
            - unbalanced [bracket
            ```
            """;

        var block = Assert.Single(QuestionBlockParser.Parse(markdown));

        Assert.True(block.IsLegacy);
        Assert.Null(block.YamlError);
    }

    [Fact]
    public void Parse_ReportsBrokenYamlInAStructuredBlockAsError()
    {
        const string markdown = """
            ```questions
            questions:
              - id: q1
                title: Broken
                options: [unclosed
            ```
            """;

        var block = Assert.Single(QuestionBlockParser.Parse(markdown));

        Assert.False(block.IsLegacy);
        Assert.NotNull(block.YamlError);
    }

    [Theory]
    [InlineData("", AnswerState.Unanswered)]
    // A present-but-null answer is not a third state. The validator rejects it; the model reads it
    // as unanswered rather than inventing a meaning for it.
    [InlineData("    answer:", AnswerState.Unanswered)]
    [InlineData("    answer: null", AnswerState.Unanswered)]
    [InlineData("    answer: jwt", AnswerState.Answered)]
    public void Parse_DistinguishesAnswerStates(string answerLine, AnswerState expected)
    {
        var markdown = $"```questions\nquestions:\n  - title: Which one?\n{answerLine}\n```";

        var question = Assert.Single(Assert.Single(QuestionBlockParser.Parse(markdown)).Block!.Questions);

        Assert.Equal(expected, question.AnswerState);
    }

    [Fact]
    public void Parse_DistinguishesScalarFromListAnswer()
    {
        const string markdown = """
            ```questions
            questions:
              - id: q1
                title: Scalar?
                answer: jwt
              - id: q2
                title: List?
                multiple: true
                answer: [jwt, sessions]
            ```
            """;

        var questions = Assert.Single(QuestionBlockParser.Parse(markdown)).Block!.Questions;

        Assert.False(questions[0].AnswerIsList);
        Assert.Equal(["jwt"], questions[0].AnswerValues);

        Assert.True(questions[1].AnswerIsList);
        Assert.Equal(["jwt", "sessions"], questions[1].AnswerValues);
    }

    [Fact]
    public void Parse_ReadsFreeTextAnswerThatMatchesNoOption()
    {
        const string markdown = """
            ```questions
            questions:
              - id: q1
                title: Which auth scheme?
                options:
                  - title: JSON Web Tokens
                    value: jwt
                  - title: Server sessions
                    value: sessions
                answer: mTLS, actually
            ```
            """;

        var question = Assert.Single(Assert.Single(QuestionBlockParser.Parse(markdown)).Block!.Questions);

        Assert.Equal(AnswerState.Answered, question.AnswerState);
        Assert.Equal(["mTLS, actually"], question.AnswerValues);
    }

    [Fact]
    public void Parse_HandlesUnterminatedFence()
    {
        const string markdown = """
            ```questions
            questions:
              - id: q1
                title: Never closed?
            """;

        var block = Assert.Single(QuestionBlockParser.Parse(markdown));

        Assert.Equal("Never closed?", block.Block!.Questions[0].Title);
    }

    [Fact]
    public void Parse_IgnoresOtherLanguages()
    {
        const string markdown = """
            ```yaml
            questions:
              - id: q1
                title: Documentation, not a question.
            ```
            """;

        Assert.Empty(QuestionBlockParser.Parse(markdown));
    }

    [Fact]
    public void FindFenceRanges_CoversFenceDelimiters()
    {
        const string markdown = """
            # A plan

            ```questions
            questions:
              - id: q1
                title: Really?
            ```

            Trailing prose.
            """;

        var range = Assert.Single(QuestionBlockParser.FindFenceRanges(markdown));

        Assert.Equal(3, range.StartLine);
        Assert.Equal(7, range.EndLine);
    }
}
