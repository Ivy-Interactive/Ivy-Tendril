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
              - title: Which auth scheme?
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
              - title: Which platforms ship first?
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
              - title: What should the feature be called?
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
              - title: Really?
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
              - title: An example, not a real question.
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
              - title: First scope question?
            ```

            ## Solution

            ```csharp
            var x = 1;
            ```

            ```questions
            questions:
              - title: Second design question?
            ```
            """;

        var blocks = QuestionBlockParser.Parse(markdown);

        Assert.Equal(2, blocks.Count);
        Assert.Equal("First scope question?", blocks[0].Block!.Questions[0].Title);
        Assert.Equal("Second design question?", blocks[1].Block!.Questions[0].Title);
        Assert.True(blocks[0].Line < blocks[1].Line);
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
              - title: Broken
                options: [unclosed
            ```
            """;

        var block = Assert.Single(QuestionBlockParser.Parse(markdown));

        Assert.False(block.IsLegacy);
        Assert.NotNull(block.YamlError);
    }

    [Theory]
    [InlineData("", AnswerState.Unanswered)]
    [InlineData("    answer:", AnswerState.Declined)]
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
              - title: Scalar?
                answer: jwt
              - title: List?
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
              - title: Which auth scheme?
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
              - title: Never closed?
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
              - title: Documentation, not a question.
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
              - title: Really?
            ```

            Trailing prose.
            """;

        var range = Assert.Single(QuestionBlockParser.FindFenceRanges(markdown));

        Assert.Equal(3, range.StartLine);
        Assert.Equal(6, range.EndLine);
    }
}
