using Ivy.Tendril.Models;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Ivy.Tendril.Helpers;

/// <summary>
///     One fenced <c>questions</c> block found in a markdown document.
/// </summary>
/// <param name="Line">1-based line number of the opening fence.</param>
/// <param name="Body">Raw fence body, dedented by the opening fence's own indentation.</param>
/// <param name="Block">The deserialized block, or null when legacy or unparseable.</param>
/// <param name="YamlError">Parse or shape error, or null. Only set for a block that was meant to be structured.</param>
/// <param name="IsLegacy">
///     True when the body is not a YAML mapping with a <c>questions</c> key — the plain-text form
///     that shipped before the schema existed. Legacy blocks warn, never error, and are never rewritten.
/// </param>
public record ParsedQuestionsBlock(
    int Line,
    string Body,
    QuestionsBlock? Block,
    string? YamlError,
    bool IsLegacy);

/// <summary>
///     Extracts fenced <c>questions</c> blocks from plan revision markdown.
///     <para>
///         Fence tracking follows CommonMark: a fence opened with N of a delimiter is closed only by
///         a run of N or more of the same delimiter, so a <c>questions</c> fence written inside a
///         longer fence is documentation, not a question. Without this, a plan that documents the
///         format fails its own validator.
///     </para>
/// </summary>
public static class QuestionBlockParser
{
    private const string InfoWord = "questions";

    /// <summary>
    ///     Strict deserializer: the schema is <c>additionalProperties: false</c>, so unlike
    ///     <see cref="YamlHelper.Deserializer" /> this one must NOT ignore unmatched properties —
    ///     an unknown key is a typo the agent has to fix, not something to swallow.
    /// </summary>
    private static readonly IDeserializer StrictDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    /// <summary>
    ///     Returns every top-level <c>questions</c> block in <paramref name="markdown" />, in
    ///     document order. A document may contain any number of blocks, anywhere.
    /// </summary>
    public static IReadOnlyList<ParsedQuestionsBlock> Parse(string markdown) =>
        Scan(markdown).Select(block => Analyze(block.StartLine, block.Body)).ToList();

    /// <summary>
    ///     1-based inclusive line ranges of every <c>questions</c> fence, delimiters included.
    ///     Used by <see cref="MarkdownLinkPolisher" /> to leave machine-read YAML alone.
    /// </summary>
    public static IReadOnlyList<QuestionFenceRange> FindFenceRanges(string markdown) =>
        Scan(markdown).Select(block => new QuestionFenceRange(block.StartLine, block.EndLine)).ToList();

    private static List<RawBlock> Scan(string markdown)
    {
        var results = new List<RawBlock>();
        if (string.IsNullOrEmpty(markdown))
            return results;

        var lines = markdown.Replace("\r\n", "\n").Split('\n');

        var open = false;
        var openChar = '\0';
        var openLength = 0;
        var openIndent = 0;
        var isQuestions = false;
        var startLine = 0;
        var body = new List<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            var fence = MatchFence(lines[i]);

            if (!open)
            {
                if (fence is not { } opening)
                    continue;

                open = true;
                openChar = opening.Delimiter;
                openLength = opening.Length;
                openIndent = opening.Indent;
                isQuestions = IsQuestionsInfo(opening.Info);
                startLine = i + 1;
                body.Clear();
                continue;
            }

            // Only a bare run of the same delimiter, at least as long as the opener, closes a fence.
            if (fence is { } closing && closing.Delimiter == openChar &&
                closing.Length >= openLength && closing.Info.Length == 0)
            {
                if (isQuestions)
                    results.Add(new RawBlock(startLine, i + 1, string.Join("\n", body)));

                open = false;
                continue;
            }

            body.Add(Dedent(lines[i], openIndent));
        }

        // An unterminated fence runs to the end of the document (CommonMark).
        if (open && isQuestions)
            results.Add(new RawBlock(startLine, lines.Length, string.Join("\n", body)));

        return results;
    }

    private static ParsedQuestionsBlock Analyze(int line, string body)
    {
        // Pass 1: the raw node, which is the only place that knows whether an `answer` key exists.
        YamlMappingNode? mapping = null;
        string? loadError = null;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(body));
            if (stream.Documents.Count > 0)
                mapping = stream.Documents[0].RootNode as YamlMappingNode;
        }
        catch (YamlException ex)
        {
            loadError = Flatten(ex.Message);
        }

        var questionsNode = mapping is not null &&
                            mapping.Children.TryGetValue(new YamlScalarNode(InfoWord), out var node)
            ? node
            : null;

        if (questionsNode is null)
        {
            // Broken YAML that clearly meant to be structured is an error; anything else is the
            // pre-schema plain-text form, which must never be rejected.
            if (loadError is not null && LooksStructured(body))
                return new ParsedQuestionsBlock(line, body, null, loadError, IsLegacy: false);

            return new ParsedQuestionsBlock(line, body, null, null, IsLegacy: true);
        }

        // Pass 2: the typed fields, strictly.
        QuestionsBlock? typed;
        try
        {
            typed = StrictDeserializer.Deserialize<QuestionsBlock>(body);
        }
        catch (YamlException ex)
        {
            return new ParsedQuestionsBlock(line, body, null, Flatten(ex.Message), IsLegacy: false);
        }

        if (typed is null)
            return new ParsedQuestionsBlock(line, body, null, "block is empty", IsLegacy: false);

        return new ParsedQuestionsBlock(
            line, body, typed with { Questions = StampAnswers(typed.Questions, questionsNode) }, null, IsLegacy: false);
    }

    /// <summary>
    ///     Copies <c>answer</c>-key presence from the raw YAML onto the deserialized questions.
    /// </summary>
    private static List<PlanQuestion> StampAnswers(List<PlanQuestion> questions, YamlNode questionsNode)
    {
        if (questionsNode is not YamlSequenceNode sequence)
            return questions;

        var answerKey = new YamlScalarNode("answer");
        var stamped = new List<PlanQuestion>(questions.Count);
        for (var i = 0; i < questions.Count; i++)
        {
            var present = i < sequence.Children.Count &&
                          sequence.Children[i] is YamlMappingNode item &&
                          item.Children.ContainsKey(answerKey);
            stamped.Add(questions[i] with { AnswerPresent = present });
        }

        return stamped;
    }

    /// <summary>The first meaningful line is a <c>questions:</c> key, so the body meant to be structured.</summary>
    private static bool LooksStructured(string body)
    {
        foreach (var raw in body.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            return line.StartsWith(InfoWord + ":", StringComparison.Ordinal);
        }

        return false;
    }

    private static bool IsQuestionsInfo(string info)
    {
        if (info.Length == 0)
            return false;

        var end = info.IndexOfAny([' ', '\t']);
        var word = end < 0 ? info : info[..end];

        // Matches the renderer, which keys off the fence language verbatim.
        return word.Equals(InfoWord, StringComparison.Ordinal);
    }

    private static string Dedent(string line, int indent)
    {
        var strip = 0;
        while (strip < indent && strip < line.Length && line[strip] == ' ')
            strip++;

        return line[strip..];
    }

    private static Fence? MatchFence(string line)
    {
        var indent = 0;
        while (indent < line.Length && indent < 4 && line[indent] == ' ')
            indent++;

        if (indent > 3 || indent >= line.Length)
            return null;

        var delimiter = line[indent];
        if (delimiter is not ('`' or '~'))
            return null;

        var end = indent;
        while (end < line.Length && line[end] == delimiter)
            end++;

        var length = end - indent;
        if (length < 3)
            return null;

        var info = line[end..].Trim();

        // A backtick fence's info string may not contain a backtick (CommonMark), which is what keeps
        // inline code like ``a ``` b`` from being read as a fence.
        if (delimiter == '`' && info.Contains('`'))
            return null;

        return new Fence(delimiter, length, indent, info);
    }

    private static string Flatten(string message) =>
        message.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();

    private readonly record struct Fence(char Delimiter, int Length, int Indent, string Info);

    private readonly record struct RawBlock(int StartLine, int EndLine, string Body);
}

/// <summary>A <c>questions</c> fence's 1-based inclusive line range, opening and closing lines included.</summary>
public readonly record struct QuestionFenceRange(int StartLine, int EndLine);
