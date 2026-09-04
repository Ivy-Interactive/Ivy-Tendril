using Ivy.Tendril.Services.Plans;

namespace Ivy.Tendril.Test;

public class QuestionValidationServiceTests
{
    private static IReadOnlyList<QuestionIssue> Validate(string body) =>
        QuestionValidationService.Validate($"```questions\n{body}\n```");

    private static string SingleError(string body)
    {
        var issue = Assert.Single(Validate(body));
        Assert.Equal(QuestionIssueSeverity.Error, issue.Severity);
        return issue.Message;
    }

    // ---------------------------------------------------------------- optional / answer: null

    [Fact]
    public void Validate_RejectsAnExplicitNullAnswer()
    {
        // `answer: null` used to mean "asked and deliberately skipped". A question that need not be
        // answered is `optional: true` now, so a null answer no longer means anything.
        var message = SingleError("""
            questions:
              - id: owner
                title: Who owns the rollout?
                answer: null
            """);

        Assert.Contains("answer: null is not a state", message);
        Assert.Contains("optional", message);
    }

    [Fact]
    public void Validate_RejectsABareAnswerKey()
    {
        // `answer:` with nothing after it is the same explicit null, written differently.
        Assert.Contains("answer: null is not a state", SingleError("""
            questions:
              - id: owner
                title: Who owns the rollout?
                answer:
            """));
    }

    [Fact]
    public void Validate_AcceptsAnOptionalQuestion()
    {
        Assert.Empty(Validate("""
            questions:
              - id: owner
                title: Who owns the rollout?
                optional: true
            """));
    }

    [Fact]
    public void Validate_AcceptsAnOptionalQuestionThatWasAnsweredAnyway()
    {
        // Optional says the plan does not wait on an answer, not that one is unwelcome.
        Assert.Empty(Validate("""
            questions:
              - id: owner
                title: Who owns the rollout?
                optional: true
                answer: platform-team
            """));
    }

    // ---------------------------------------------------------------- clean documents

    [Fact]
    public void Validate_ReturnsNothingForAValidDocument()
    {
        const string markdown = """
            # A plan

            ```questions
            questions:
              - id: q1
                title: Which auth scheme?
                header: Auth
                other: false
                options:
                  - title: JSON Web Tokens
                    value: jwt
                    recommended: true
                  - title: Server sessions
                    value: sessions
                answer: jwt
              - id: q2
                title: What should it be called?
            ```

            ## Solution

            Prose.
            """;

        Assert.Empty(QuestionValidationService.Validate(markdown));
    }

    [Fact]
    public void Validate_ReturnsNothingForADocumentWithNoQuestionBlocks()
    {
        Assert.Empty(QuestionValidationService.Validate("# A plan\n\nJust prose and a ```csharp fence.\n"));
    }

    [Fact]
    public void Validate_IgnoresQuestionsFenceNestedInLongerFence()
    {
        const string markdown = """"
            ````
            ```questions
            questions:
              - id: q1
                title: Documentation, with five options and every rule broken.
                options:
                  - title: Other
                    value: NOPE
            ```
            ````
            """";

        Assert.Empty(QuestionValidationService.Validate(markdown));
    }

    // ---------------------------------------------------------------- lint rules

    [Fact]
    public void Validate_RejectsMoreThanOneRecommendedOption()
    {
        var message = SingleError("""
            questions:
              - id: q1
                title: Which one?
                options:
                  - title: First
                    value: first
                    recommended: true
                  - title: Second
                    value: second
                    recommended: true
            """);

        Assert.Equal("question 1: more than one option is recommended", message);
    }

    [Theory]
    [InlineData("Other")]
    [InlineData("Something else")]
    [InlineData("Custom")]
    public void Validate_RejectsHandAuthoredOtherOption(string title)
    {
        var message = SingleError($"""
            questions:
              - id: q1
                title: Which one?
                options:
                  - title: First
                    value: first
                  - title: {title}
                    value: something-else
            """);

        Assert.Equal($"question 1: option '{title}' duplicates what other: true provides", message);
    }

    [Fact]
    public void Validate_RejectsOtherFalseWithNoOptions()
    {
        var message = SingleError("""
            questions:
              - id: q1
                title: Which one?
                other: false
            """);

        Assert.Equal("question 1: other: false with no options is unanswerable", message);
    }

    [Fact]
    public void Validate_RejectsScalarAnswerWhenMultipleIsTrue()
    {
        var message = SingleError("""
            questions:
              - id: q1
                title: Which ones?
                multiple: true
                answer: first
            """);

        Assert.Equal("question 1: multiple: true requires a list answer", message);
    }

    [Fact]
    public void Validate_RejectsListAnswerWhenMultipleIsFalse()
    {
        var message = SingleError("""
            questions:
              - id: q1
                title: Which one?
                answer: [first, second]
            """);

        Assert.Equal("question 1: answer must be a scalar when multiple is false", message);
    }

    [Fact]
    public void Validate_RejectsDuplicateOptionValue()
    {
        var message = SingleError("""
            questions:
              - id: q1
                title: Which one?
                options:
                  - title: First
                    value: jwt
                  - title: Second
                    value: jwt
            """);

        Assert.Equal("question 1: duplicate option value 'jwt'", message);
    }

    [Fact]
    public void Validate_RejectsUnmatchedAnswerWhenOtherIsFalse()
    {
        var message = SingleError("""
            questions:
              - id: q1
                title: Which one?
                other: false
                options:
                  - title: First
                    value: first
                  - title: Second
                    value: second
                answer: third
            """);

        Assert.Equal("question 1: answer 'third' matches no option and other is false", message);
    }

    [Fact]
    public void Validate_AllowsUnmatchedAnswerWhenOtherIsTrue()
    {
        Assert.Empty(Validate("""
            questions:
              - id: q1
                title: Which one?
                options:
                  - title: First
                    value: first
                  - title: Second
                    value: second
                answer: something the user typed
            """));
    }

    // ---------------------------------------------------------------- schema bounds

    [Fact]
    public void Validate_RejectsBlockWithNoQuestions()
    {
        var message = SingleError("questions: []");

        Assert.Equal("at least one question is required", message);
    }

    [Fact]
    public void Validate_RejectsMoreThanFourQuestions()
    {
        var body = "questions:\n" + string.Join("\n",
            Enumerable.Range(1, 5).Select(i => $"  - id: q{i}\n    title: Question {i}?"));

        Assert.Equal("5 questions in one block; at most 4 are allowed", SingleError(body));
    }

    [Fact]
    public void Validate_AcceptsFourQuestions()
    {
        var body = "questions:\n" + string.Join("\n",
            Enumerable.Range(1, 4).Select(i => $"  - id: q{i}\n    title: Question {i}?"));

        Assert.Empty(Validate(body));
    }

    [Fact]
    public void Validate_RejectsSingleOption()
    {
        var message = SingleError("""
            questions:
              - id: q1
                title: Which one?
                options:
                  - title: Only
                    value: only
            """);

        Assert.Equal("question 1: 1 option; at least 2 are required", message);
    }

    [Fact]
    public void Validate_RejectsMoreThanFourOptions()
    {
        var body = "questions:\n  - id: q1\n    title: Which one?\n    options:\n" + string.Join("\n",
            Enumerable.Range(1, 5).Select(i => $"      - title: Option {i}\n        value: option-{i}"));

        Assert.Equal("question 1: 5 options; at most 4 are allowed", SingleError(body));
    }

    [Fact]
    public void Validate_RejectsHeaderLongerThanTwelveCharacters()
    {
        var message = SingleError("""
            questions:
              - id: q1
                title: Which one?
                header: ThirteenChars
            """);

        Assert.Equal("question 1: header 'ThirteenChars' is 13 characters; at most 12 are allowed", message);
    }

    [Fact]
    public void Validate_AcceptsTwelveCharacterHeader()
    {
        Assert.Empty(Validate("""
            questions:
              - id: q1
                title: Which one?
                header: TwelveChars!
            """));
    }

    [Fact]
    public void Validate_RejectsMissingTitle()
    {
        var message = SingleError("""
            questions:
              - id: q1
                header: Auth
            """);

        Assert.Equal("question 1: title is required", message);
    }

    [Fact]
    public void Validate_RejectsMissingId()
    {
        var message = SingleError("""
            questions:
              - title: Which one?
            """);

        Assert.Equal("question 1: id is required", message);
    }

    [Fact]
    public void Validate_RejectsBlankId()
    {
        var message = SingleError("""
            questions:
              - id: "   "
                title: Which one?
            """);

        Assert.Equal("question 1: id is required", message);
    }

    [Fact]
    public void Validate_RejectsDuplicateIdWithinOneBlock()
    {
        var message = SingleError("""
            questions:
              - id: scope
                title: Which one?
              - id: scope
                title: And which one?
            """);

        Assert.Equal("question 2: duplicate question id 'scope'", message);
    }

    [Theory]
    [InlineData("JWT")]
    [InlineData("-jwt")]
    [InlineData("json web tokens")]
    [InlineData("json_web_tokens")]
    public void Validate_RejectsOptionValueThatIsNotASlug(string value)
    {
        var message = SingleError($"""
            questions:
              - id: q1
                title: Which one?
                options:
                  - title: First
                    value: {value}
                  - title: Second
                    value: second
            """);

        Assert.Equal($"question 1: option value '{value}' must match ^[a-z0-9][a-z0-9-]*$", message);
    }

    [Fact]
    public void Validate_AcceptsSlugOptionValues()
    {
        Assert.Empty(Validate("""
            questions:
              - id: q1
                title: Which one?
                options:
                  - title: First
                    value: json-web-tokens
                  - title: Second
                    value: 2fa
            """));
    }

    [Fact]
    public void Validate_RejectsUnknownKeyOnQuestion()
    {
        var message = SingleError("""
            questions:
              - id: q1
                title: Which one?
                requird: true
            """);

        Assert.StartsWith("invalid questions YAML:", message);
        Assert.Contains("requird", message);
    }

    [Fact]
    public void Validate_RejectsUnknownKeyOnOption()
    {
        var message = SingleError("""
            questions:
              - id: q1
                title: Which one?
                options:
                  - title: First
                    value: first
                    reccommended: true
                  - title: Second
                    value: second
            """);

        Assert.StartsWith("invalid questions YAML:", message);
        Assert.Contains("reccommended", message);
    }

    // ---------------------------------------------------------------- legacy blocks

    [Fact]
    public void Validate_WarnsOnceForALegacyProseBlock()
    {
        var issue = Assert.Single(Validate("Should this use JWTs or server sessions?"));

        Assert.Equal(QuestionIssueSeverity.Warning, issue.Severity);
        Assert.Contains("free-text questions block", issue.Message);
    }

    [Fact]
    public void Validate_WarnsRatherThanErringForAMappingWithoutQuestions()
    {
        var issue = Assert.Single(Validate("open: should we use JWTs?"));

        Assert.Equal(QuestionIssueSeverity.Warning, issue.Severity);
    }

    // ---------------------------------------------------------------- wrapper-less shapes
    //
    // These used to be read as legacy prose, which meant the lint rules never ran on them: a block
    // with colliding ids or a bad option value warned once and shipped.

    [Fact]
    public void Validate_AcceptsABareSequenceWrittenWithoutTheQuestionsKey()
    {
        Assert.Empty(Validate("""
            - id: caching-strategy
              title: Which caching strategy should we use?
              options:
                - title: In-Memory
                  value: in-memory
                - title: Distributed Redis
                  value: redis
            """));
    }

    [Fact]
    public void Validate_AcceptsASingleQuestionWrittenWithoutAnyWrapper()
    {
        Assert.Empty(Validate("""
            id: confirmation-prompt
            title: Should we prompt before deleting?
            """));
    }

    [Fact]
    public void Validate_LintsABareSequenceLikeAnyOtherBlock()
    {
        Assert.Contains("duplicate option value 'redis'", SingleError("""
            - id: caching-strategy
              title: Which caching strategy should we use?
              options:
                - title: Redis
                  value: redis
                - title: Redis again
                  value: redis
            """));
    }

    [Fact]
    public void Validate_LintsASingleQuestionWrittenWithoutAnyWrapper()
    {
        Assert.Contains("title is required", SingleError("""
            id: confirmation-prompt
            other: true
            """));
    }

    [Fact]
    public void Validate_CatchesIdsCollidingAcrossShapes()
    {
        // Ids are addressed document-wide, so the shape a block was written in changes nothing.
        var issues = QuestionValidationService.Validate("""
            ```questions
            questions:
              - id: same
                title: First?
            ```

            ```questions
            - id: same
              title: Second?
            ```
            """);

        var issue = Assert.Single(issues);
        Assert.Equal(QuestionIssueSeverity.Error, issue.Severity);
        Assert.Contains("duplicate question id 'same'", issue.Message);
    }

    [Fact]
    public void Validate_ReportsAnEmptyQuestionsKeyRatherThanThrowing()
    {
        // `questions:` with nothing under it deserializes the list to null. It reaches the validator
        // as an empty block, which is a thing the agent can be told to fix.
        Assert.Contains("at least one question is required", SingleError("questions:"));
    }

    // ---------------------------------------------------------------- multiple blocks

    [Fact]
    public void Validate_AcceptsSeveralValidBlocksAtDifferentPoints()
    {
        const string markdown = """
            # A plan

            ```questions
            questions:
              - id: q1
                title: A scope question?
            ```

            ## Solution

            ```questions
            questions:
              - id: q2
                title: A design question?
            ```
            """;

        Assert.Empty(QuestionValidationService.Validate(markdown));
    }

    [Fact]
    public void Validate_RejectsAnIdReusedByAnotherBlock()
    {
        // An answer travels as an id and nothing else, so the same id in two blocks would leave it
        // ambiguous which question was answered. Only this service sees the whole document.
        const string markdown = """
            ```questions
            questions:
              - id: scope
                title: A scope question?
            ```

            ## Solution

            ```questions
            questions:
              - id: scope
                title: A design question?
            ```
            """;

        var issue = Assert.Single(QuestionValidationService.Validate(markdown));

        Assert.Equal("block 2: question 1: duplicate question id 'scope'", issue.Message);
        Assert.Equal(9, issue.Line);
    }

    [Fact]
    public void Validate_NumbersEachBlockWhenThereIsMoreThanOne()
    {
        const string markdown = """
            ```questions
            questions:
              - id: q1
                title: Fine?
              - id: q2
                title: Also fine?
                other: false
            ```

            ```questions
            questions:
              - id: q3
                title: Broken?
                header: WayTooLongHeader
            ```
            """;

        var issues = QuestionValidationService.Validate(markdown);

        Assert.Equal(2, issues.Count);
        Assert.Equal("block 1: question 2: other: false with no options is unanswerable", issues[0].Message);
        Assert.StartsWith("block 2: question 1: header 'WayTooLongHeader'", issues[1].Message);
        Assert.Equal(1, issues[0].Line);
        Assert.Equal(10, issues[1].Line);
    }

    [Fact]
    public void Validate_OmitsBlockPrefixForASingleBlock()
    {
        Assert.DoesNotContain("block 1", SingleError("""
            questions:
              - id: q1
                title: Which one?
                other: false
            """));
    }

    [Fact]
    public void QuestionIssue_RendersWithItsSourceLine()
    {
        var issue = new QuestionIssue(QuestionIssueSeverity.Error, 12, "question 2: duplicate option value 'jwt'");

        Assert.Equal("line 12: question 2: duplicate option value 'jwt'", issue.ToString());
    }

    [Fact]
    public void Validate_ReportsEveryProblemAtOnce()
    {
        var issues = Validate("""
            questions:
              - id: q1
                title: Which one?
                header: FarTooLongToFit
                options:
                  - title: First
                    value: jwt
                    recommended: true
                  - title: Second
                    value: jwt
                    recommended: true
            """);

        Assert.Equal(3, issues.Count);
        Assert.All(issues, i => Assert.Equal(QuestionIssueSeverity.Error, i.Severity));
        Assert.Contains(issues, i => i.Message.Contains("at most 12 are allowed"));
        Assert.Contains(issues, i => i.Message.Contains("duplicate option value 'jwt'"));
        Assert.Contains(issues, i => i.Message.Contains("more than one option is recommended"));
    }
}
