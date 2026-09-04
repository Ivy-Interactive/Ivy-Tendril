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
///     True when the body is none of the shapes a questions block may take — the plain-text form
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
    ///     First lines that announce a structured block, one per shape <see cref="Classify" /> reads.
    /// </summary>
    private static readonly string[] StructuredOpeners = [InfoWord + ":", "- id:", "id:"];

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
        YamlNode? root = null;
        string? loadError = null;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(body));
            if (stream.Documents.Count > 0)
                root = stream.Documents[0].RootNode;
        }
        catch (YamlException ex)
        {
            loadError = Flatten(ex.Message);
        }

        if (Classify(root) is not { } classified)
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
            typed = Deserialize(body, classified.Shape);
        }
        catch (YamlException ex)
        {
            return new ParsedQuestionsBlock(line, body, null, Flatten(ex.Message), IsLegacy: false);
        }

        if (typed is null)
            return new ParsedQuestionsBlock(line, body, null, "block is empty", IsLegacy: false);

        return new ParsedQuestionsBlock(
            line, body, typed with { Questions = StampAnswers(typed.Questions, classified.Entries) }, null, IsLegacy: false);
    }

    /// <summary>How a block spells its questions. All three mean the same thing.</summary>
    private enum RootShape
    {
        /// <summary>The canonical form: a mapping whose <c>questions</c> key holds the list.</summary>
        Mapping,

        /// <summary>A bare sequence — the list written without its <c>questions:</c> wrapper.</summary>
        Sequence,

        /// <summary>One question mapping, written without either wrapper.</summary>
        SingleQuestion
    }

    /// <summary>
    ///     Which shape <paramref name="root" /> is, and the question nodes inside it, or null when it
    ///     is no shape at all — a legacy block.
    ///     <para>
    ///         The two wrapper-less shapes exist because agents keep emitting them: a fence already
    ///         says <c>questions</c>, so repeating the word inside reads as redundant, and the block
    ///         that comes back is a picker the user cannot use. Accepting them costs nothing and is
    ///         the difference between a question being answerable and being a code listing.
    ///     </para>
    ///     <para>
    ///         What keeps a legacy bullet list of prose from being read as a wrapper-less sequence is
    ///         that every item must look like a question. Nothing weaker would do: legacy blocks warn,
    ///         structured ones block the write, so mistaking one for the other fails a plan over
    ///         formatting.
    ///     </para>
    /// </summary>
    private static (RootShape Shape, IReadOnlyList<YamlNode> Entries)? Classify(YamlNode? root) => root switch
    {
        // A `questions` key whose value is not a sequence stays here rather than falling through to
        // the shapes below: it is a structured block with the wrong value, and deserializing it says
        // so far better than a legacy warning would.
        YamlMappingNode mapping when mapping.Children.TryGetValue(new YamlScalarNode(InfoWord), out var node) =>
            (RootShape.Mapping, node is YamlSequenceNode questions ? [.. questions.Children] : Array.Empty<YamlNode>()),

        YamlSequenceNode sequence when sequence.Children.Count > 0 && sequence.Children.All(LooksLikeQuestion) =>
            (RootShape.Sequence, [.. sequence.Children]),

        YamlMappingNode single when LooksLikeQuestion(single) =>
            (RootShape.SingleQuestion, new YamlNode[] { single }),

        _ => null
    };

    /// <summary>
    ///     A mapping that carries an <c>id</c>, which is the one field a question cannot do without —
    ///     an answer travels as an id and a value, so a mapping lacking one could never be answered
    ///     whatever else it holds. It is also the conservative test: everything it turns away stays a
    ///     legacy block, which warns, rather than becoming a structured block that blocks the write.
    /// </summary>
    private static bool LooksLikeQuestion(YamlNode node) =>
        node is YamlMappingNode mapping && mapping.Children.ContainsKey(new YamlScalarNode("id"));

    private static QuestionsBlock? Deserialize(string body, RootShape shape)
    {
        switch (shape)
        {
            case RootShape.Mapping:
                if (StrictDeserializer.Deserialize<QuestionsBlock>(body) is not { } block)
                    return null;

                // A bare `questions:` with nothing under it deserializes the property to null,
                // overwriting its initializer. Normalized here so an empty block reaches the
                // validator and is told what it is missing, rather than throwing on the way.
                List<PlanQuestion>? questions = block.Questions;
                return block with { Questions = questions ?? [] };

            case RootShape.Sequence:
                return new QuestionsBlock
                {
                    Questions = StrictDeserializer.Deserialize<List<PlanQuestion>>(body) ?? []
                };

            default:
                return StrictDeserializer.Deserialize<PlanQuestion>(body) is { } single
                    ? new QuestionsBlock { Questions = [single] }
                    : null;
        }
    }

    /// <summary>
    ///     Copies <c>answer</c>-key presence from the raw YAML onto the deserialized questions.
    /// </summary>
    private static List<PlanQuestion> StampAnswers(List<PlanQuestion> questions, IReadOnlyList<YamlNode> entries)
    {
        var answerKey = new YamlScalarNode("answer");
        var stamped = new List<PlanQuestion>(questions.Count);
        for (var i = 0; i < questions.Count; i++)
        {
            var present = i < entries.Count &&
                          entries[i] is YamlMappingNode item &&
                          item.Children.ContainsKey(answerKey);
            stamped.Add(questions[i] with { AnswerPresent = present });
        }

        return stamped;
    }

    /// <summary>
    ///     The first meaningful line opens one of the shapes above, so the body meant to be
    ///     structured — and broken YAML in it is an error rather than a legacy block.
    /// </summary>
    private static bool LooksStructured(string body)
    {
        foreach (var raw in body.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            return StructuredOpeners.Any(opener => line.StartsWith(opener, StringComparison.Ordinal));
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
