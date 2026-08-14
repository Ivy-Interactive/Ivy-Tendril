using System.Text.RegularExpressions;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Services.Plans;

/// <summary>How much a question-block problem matters.</summary>
public enum QuestionIssueSeverity
{
    /// <summary>Reported to the caller, never blocks the write.</summary>
    Warning,

    /// <summary>Blocks the write. The agent has to fix it and retry.</summary>
    Error
}

/// <summary>One problem found in a <c>questions</c> block, located by the line of its opening fence.</summary>
public record QuestionIssue(QuestionIssueSeverity Severity, int Line, string Message)
{
    /// <summary>Renders as <c>line 12: question 2: duplicate option value 'jwt'</c>.</summary>
    public override string ToString() => $"line {Line}: {Message}";
}

/// <summary>
///     Validates fenced <c>questions</c> blocks in plan revision markdown against the schema and lint
///     rules in <c>Prompts/Plans.md</c> (<c>## Question Blocks</c>).
///     <para>
///         Unlike <see cref="PlanValidationService" /> this reports every problem at once instead of
///         throwing on the first, because the caller is an agent that has to fix them all in one edit.
///     </para>
/// </summary>
public static class QuestionValidationService
{
    private static readonly Regex SlugRegex = new("^[a-z0-9][a-z0-9-]*$", RegexOptions.Compiled);

    /// <summary>Titles that restate what <c>other: true</c> already provides.</summary>
    private static readonly string[] ReservedOptionTitles = ["Other", "Something else", "Custom"];

    private const int MaxQuestionsPerBlock = 4;
    private const int MinOptions = 2;
    private const int MaxOptions = 4;
    private const int MaxHeaderLength = 12;

    /// <summary>
    ///     Returns every issue in every <c>questions</c> block of <paramref name="markdown" />, in
    ///     document order. An empty result means the document is clean.
    /// </summary>
    public static IReadOnlyList<QuestionIssue> Validate(string markdown)
    {
        var blocks = QuestionBlockParser.Parse(markdown);
        var issues = new List<QuestionIssue>();

        // "block N:" is noise when there is only one block, and essential when there are several.
        var numbered = blocks.Count > 1;

        // Question ids are addressed document-wide, not per block: an answer travels as an id and a
        // value with no block alongside it, so two blocks reusing one id would leave it ambiguous
        // which question was answered. This is the only component that sees every block at once, so
        // the set spans them all instead of being reset per block.
        var ids = new HashSet<string>(StringComparer.Ordinal);

        for (var b = 0; b < blocks.Count; b++)
        {
            var block = blocks[b];
            var prefix = numbered ? $"block {b + 1}: " : "";

            if (block.IsLegacy)
            {
                issues.Add(new QuestionIssue(QuestionIssueSeverity.Warning, block.Line,
                    prefix + "free-text questions block predating the schema; rewrite it as a questions mapping (see Question Blocks in Plans.md)"));
                continue;
            }

            if (block.YamlError is not null)
            {
                issues.Add(Error(block.Line, prefix + $"invalid questions YAML: {block.YamlError}"));
                continue;
            }

            ValidateBlock(block.Block!, block.Line, prefix, ids, issues);
        }

        return issues;
    }

    private static void ValidateBlock(QuestionsBlock block, int line, string prefix, HashSet<string> ids, List<QuestionIssue> issues)
    {
        var questions = block.Questions;

        if (questions.Count == 0)
        {
            issues.Add(Error(line, prefix + "at least one question is required"));
            return;
        }

        if (questions.Count > MaxQuestionsPerBlock)
            issues.Add(Error(line, prefix + $"{questions.Count} questions in one block; at most {MaxQuestionsPerBlock} are allowed"));

        for (var n = 0; n < questions.Count; n++)
            ValidateQuestion(questions[n], line, $"{prefix}question {n + 1}: ", ids, issues);
    }

    private static void ValidateQuestion(PlanQuestion question, int line, string prefix, HashSet<string> ids, List<QuestionIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(question.Id))
            issues.Add(Error(line, prefix + "id is required"));
        else if (!ids.Add(question.Id))
            issues.Add(Error(line, prefix + $"duplicate question id '{question.Id}'"));

        if (string.IsNullOrWhiteSpace(question.Title))
            issues.Add(Error(line, prefix + "title is required"));

        if (question.Header is { } header && header.Length > MaxHeaderLength)
            issues.Add(Error(line, prefix + $"header '{header}' is {header.Length} characters; at most {MaxHeaderLength} are allowed"));

        var options = question.Options;
        if (options is null || options.Count == 0)
        {
            if (!question.Other)
                issues.Add(Error(line, prefix + "other: false with no options is unanswerable"));
        }
        else
        {
            ValidateOptions(options, line, prefix, issues);
        }

        ValidateAnswer(question, line, prefix, issues);
    }

    private static void ValidateOptions(List<QuestionOption> options, int line, string prefix, List<QuestionIssue> issues)
    {
        if (options.Count < MinOptions)
            issues.Add(Error(line, prefix + $"{options.Count} option; at least {MinOptions} are required"));

        if (options.Count > MaxOptions)
            issues.Add(Error(line, prefix + $"{options.Count} options; at most {MaxOptions} are allowed"));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var recommended = 0;

        foreach (var option in options)
        {
            if (string.IsNullOrWhiteSpace(option.Title))
                issues.Add(Error(line, prefix + "option title is required"));
            else if (ReservedOptionTitles.Contains(option.Title.Trim(), StringComparer.OrdinalIgnoreCase))
                issues.Add(Error(line, prefix + $"option '{option.Title}' duplicates what other: true provides"));

            if (string.IsNullOrWhiteSpace(option.Value))
                issues.Add(Error(line, prefix + $"option '{option.Title}' is missing a value"));
            else if (!SlugRegex.IsMatch(option.Value))
                issues.Add(Error(line, prefix + $"option value '{option.Value}' must match ^[a-z0-9][a-z0-9-]*$"));
            else if (!seen.Add(option.Value))
                issues.Add(Error(line, prefix + $"duplicate option value '{option.Value}'"));

            if (option.Recommended)
                recommended++;
        }

        if (recommended > 1)
            issues.Add(Error(line, prefix + "more than one option is recommended"));
    }

    private static void ValidateAnswer(PlanQuestion question, int line, string prefix, List<QuestionIssue> issues)
    {
        if (question.AnswerState != AnswerState.Answered)
            return;

        if (question.Multiple && !question.AnswerIsList)
        {
            issues.Add(Error(line, prefix + "multiple: true requires a list answer"));
        }
        else if (!question.Multiple && question.AnswerIsList)
        {
            issues.Add(Error(line, prefix + "answer must be a scalar when multiple is false"));
        }

        if (question.Other || question.Options is not { Count: > 0 } options)
            return;

        foreach (var entry in question.AnswerValues)
        {
            if (!options.Any(o => string.Equals(o.Value, entry, StringComparison.Ordinal)))
                issues.Add(Error(line, prefix + $"answer '{entry}' matches no option and other is false"));
        }
    }

    private static QuestionIssue Error(int line, string message) =>
        new(QuestionIssueSeverity.Error, line, message);
}
