using System.Globalization;
using System.Text;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace Ivy.Tendril.Widgets;

/// <summary>One fenced <c>questions</c> block located in a markdown document.</summary>
/// <param name="Index">0-based, document order.</param>
/// <param name="BodyStart">Offset into the markdown of the first body character.</param>
/// <param name="BodyEnd">Offset just past the last body character.</param>
/// <param name="Body">
///     The body verbatim — <c>markdown[BodyStart..BodyEnd]</c>. Includes the newline that terminates
///     the last body line, and excludes both fence lines.
/// </param>
public readonly record struct QuestionBlockSource(int Index, int BodyStart, int BodyEnd, string Body);

/// <summary>
///     One question as an index entry: what it is called, where it lives, and whether it has been
///     dealt with. Enough to build a table of contents beside a long plan.
/// </summary>
/// <param name="BlockIndex">0-based index of the <c>questions</c> fence holding it.</param>
/// <param name="Id">The question's <c>id</c> — unique across the revision, and what
/// <see cref="DraftMarkdown.ScrollTo" /> addresses.</param>
/// <param name="Title">The question itself. Empty when the block omitted it.</param>
/// <param name="Header">The optional short label shown as an eyebrow above the title.</param>
/// <param name="HasAnswer">Whether the question carries an <c>answer</c>.</param>
/// <param name="IsOptional">
///     Whether the plan is complete without an answer. An index treats an unanswered optional
///     question as settled, so what remains outstanding is what still wants a human.
/// </param>
public readonly record struct QuestionSummary(
    int BlockIndex,
    string Id,
    string Title,
    string? Header,
    bool HasAnswer,
    bool IsOptional);

/// <summary>
///     Merges a <see cref="QuestionAnswer" /> reported by <see cref="DraftMarkdown.OnAnswersChange" />
///     back into the markdown it came from.
///     <para>
///         The widget never rewrites its own document: it reports what changed and the host decides
///         how and whether to persist it. This is that merge, for hosts that do want to persist —
///         the C# counterpart of <c>setAnswer</c> in <c>questionsSource.ts</c>.
///     </para>
///     <para>
///         The edit is surgical. Only the <c>answer</c> key of the addressed question is inserted,
///         replaced or removed; every other byte of the document — comments, key order, scalar style,
///         CRLF line endings, and the other blocks — is left exactly as it was. Nothing is
///         re-serialized, because reformatting a plan the agent wrote is a change the user did not ask
///         for.
///     </para>
///     <para>
///         A question is addressed by <c>id</c> alone, with no block index, because the schema
///         requires ids to be unique across the whole revision — see the <c>## Question Blocks</c>
///         section of <c>Prompts/Plans.md</c>. Should a malformed document reuse one anyway, the first
///         block in document order wins.
///     </para>
/// </summary>
public static class QuestionAnswers
{
    private const string InfoWord = "questions";
    private const string AnswerKey = "answer";

    /// <summary>Every top-level <c>questions</c> block in <paramref name="markdown" />, in document order.</summary>
    public static IReadOnlyList<QuestionBlockSource> Scan(string markdown) =>
        ScanBlocks(markdown)
            .Select(b => new QuestionBlockSource(b.Index, b.BodyStart, b.BodyEnd, b.Body))
            .ToList();

    /// <summary>
    ///     Every question in the document, in document order — enough to build an index of them
    ///     without the host parsing YAML itself.
    ///     <para>
    ///         Tolerant, like the widget's own reader: a block that does not parse, or is the
    ///         pre-schema plain-text form, contributes nothing rather than throwing.
    ///     </para>
    /// </summary>
    public static IReadOnlyList<QuestionSummary> Read(string markdown)
    {
        var summaries = new List<QuestionSummary>();
        if (string.IsNullOrEmpty(markdown))
            return summaries;

        foreach (var block in ScanBlocks(markdown))
        {
            var body = Dedent(block.Body, block.Indent);

            YamlMappingNode? root;
            try
            {
                var stream = new YamlStream();
                stream.Load(new StringReader(body));
                root = stream.Documents.Count > 0 ? stream.Documents[0].RootNode as YamlMappingNode : null;
            }
            catch (YamlException)
            {
                continue;
            }

            if (root is null ||
                !root.Children.TryGetValue(new YamlScalarNode(InfoWord), out var node) ||
                node is not YamlSequenceNode questions)
                continue;

            foreach (var child in questions.Children)
            {
                if (child is not YamlMappingNode entry)
                    continue;

                var id = Value(entry, "id");
                if (string.IsNullOrEmpty(id))
                    continue;

                // A present-but-null answer is not a state the schema has, so it reads as
                // unanswered rather than as something in between.
                var answer = Entry(entry, AnswerKey);
                summaries.Add(new QuestionSummary(
                    block.Index,
                    id,
                    Value(entry, "title") ?? "",
                    Value(entry, "header"),
                    answer is { } pair && !IsNullScalar(pair.Value),
                    string.Equals(Value(entry, "optional"), "true", StringComparison.OrdinalIgnoreCase)));
            }
        }

        return summaries;
    }

    /// <summary>An explicit YAML null — <c>answer: null</c>, <c>answer: ~</c> or a bare <c>answer:</c>.</summary>
    private static bool IsNullScalar(YamlNode node) =>
        node is YamlScalarNode { Style: ScalarStyle.Plain } scalar &&
        (string.IsNullOrEmpty(scalar.Value) ||
         string.Equals(scalar.Value, "null", StringComparison.OrdinalIgnoreCase) ||
         scalar.Value == "~");

    /// <summary>
    ///     Returns <paramref name="markdown" /> with <paramref name="answer" /> written onto the
    ///     question it names.
    ///     <para>
    ///         <c>null</c> or an empty <see cref="QuestionAnswer.Answer" /> removes the <c>answer</c>
    ///         key, taking the question back to unanswered; a non-empty list is the answer itself.
    ///         Whether that is written as a scalar or a list follows the question's own
    ///         <c>multiple</c>, so the result satisfies the "answer is a list iff multiple" lint rule.
    ///     </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">No question in the document has that id.</exception>
    public static string Apply(string markdown, QuestionAnswer answer)
    {
        if (TryApply(markdown, answer, out var updated))
            return updated;

        throw new InvalidOperationException(
            $"No questions block in the document has a question with id '{answer.QuestionId}'.");
    }

    /// <summary>
    ///     As <see cref="Apply" />, but reports a miss instead of throwing. Use this when the document
    ///     may have moved on since the widget rendered it — a stale answer is worth ignoring, not worth
    ///     an exception in an event handler.
    /// </summary>
    /// <returns>False, with <paramref name="updated" /> set to <paramref name="markdown" />, on a miss.</returns>
    public static bool TryApply(string markdown, QuestionAnswer answer, out string updated)
    {
        updated = markdown;
        if (string.IsNullOrEmpty(markdown) || string.IsNullOrEmpty(answer.QuestionId))
            return false;

        foreach (var block in ScanBlocks(markdown))
        {
            // Parsing needs the body at column 0; the result is re-indented before it goes back.
            var body = Dedent(block.Body, block.Indent);
            if (!TryEditBody(body, answer, out var editedBody))
                continue;

            updated = markdown[..block.BodyStart]
                      + Reindent(editedBody, block.Indent)
                      + markdown[block.BodyEnd..];
            return true;
        }

        return false;
    }

    // ---------------------------------------------------------------------------------------------
    // Editing one block body
    // ---------------------------------------------------------------------------------------------

    private static bool TryEditBody(string body, QuestionAnswer answer, out string edited)
    {
        edited = body;

        YamlMappingNode? root;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(body));
            root = stream.Documents.Count > 0 ? stream.Documents[0].RootNode as YamlMappingNode : null;
        }
        catch (YamlException)
        {
            // A block the widget could not render either. Leave it alone and keep looking.
            return false;
        }

        if (root is null ||
            !root.Children.TryGetValue(new YamlScalarNode(InfoWord), out var node) ||
            node is not YamlSequenceNode questions)
            return false;

        foreach (var child in questions.Children)
        {
            if (child is not YamlMappingNode entry || !HasId(entry, answer.QuestionId))
                continue;

            edited = entry.Style == MappingStyle.Flow
                ? EditFlowEntry(body, entry, answer.Answer)
                : EditBlockEntry(body, entry, answer.Answer);
            return true;
        }

        return false;
    }

    private static bool HasId(YamlMappingNode entry, string questionId) =>
        Value(entry, "id") is { } id && string.Equals(id, questionId, StringComparison.Ordinal);

    private static bool IsMultiple(YamlMappingNode entry) =>
        string.Equals(Value(entry, "multiple"), "true", StringComparison.OrdinalIgnoreCase);

    private static string? Value(YamlMappingNode entry, string key) =>
        Entry(entry, key)?.Value is YamlScalarNode scalar ? scalar.Value : null;

    private static KeyValuePair<YamlNode, YamlNode>? Entry(YamlMappingNode entry, string key)
    {
        foreach (var pair in entry.Children)
        {
            if (pair.Key is YamlScalarNode scalar && string.Equals(scalar.Value, key, StringComparison.Ordinal))
                return pair;
        }

        return null;
    }

    /// <summary>The ordinary case: a block mapping, one key per line.</summary>
    private static string EditBlockEntry(string body, YamlMappingNode entry, IReadOnlyList<string>? answer)
    {
        var newline = DetectNewline(body);
        var indent = KeyIndent(entry);
        var existing = Entry(entry, AnswerKey);

        if (existing is not { } pair)
        {
            if (answer is not { Count: > 0 })
                return body; // Already unanswered — nothing to remove.

            var insertAt = EndOfBlockEntry(body, entry);
            var text = newline + new string(' ', indent) +
                       FormatBlockAnswer(answer, IsMultiple(entry), indent, newline);
            return body[..insertAt] + text + body[insertAt..];
        }

        var keyStart = (int)pair.Key.Start.Index;
        var valueEnd = EndOfBlockValue(body, pair.Value, indent);

        if (answer is not { Count: > 0 })
        {
            // Take the whole line, its indentation and its terminator with it, so removing an answer
            // leaves no blank line behind.
            var lineStart = StartOfLine(body, keyStart);
            var lineEnd = EndOfLineIncludingTerminator(body, valueEnd);
            return body[..lineStart] + body[lineEnd..];
        }

        var replacement = FormatBlockAnswer(answer, IsMultiple(entry), indent, newline);
        return body[..keyStart] + replacement + body[valueEnd..];
    }

    /// <summary>The `- {id: x, title: y}` case, where the whole question is one flow mapping.</summary>
    private static string EditFlowEntry(string body, YamlMappingNode entry, IReadOnlyList<string>? answer)
    {
        var existing = Entry(entry, AnswerKey);

        if (existing is not { } pair)
        {
            if (answer is not { Count: > 0 })
                return body;

            var close = MatchDelimiter(body, (int)entry.Start.Index);
            var text = $", {AnswerKey}: {FormatFlowValue(answer, IsMultiple(entry))}";
            return body[..close] + text + body[close..];
        }

        var keyStart = (int)pair.Key.Start.Index;
        var valueEnd = EndOfFlowValue(body, pair.Value);

        if (answer is not { Count: > 0 })
        {
            // `id` always precedes `answer`, so there is a separating comma to absorb.
            var start = keyStart;
            var scan = keyStart - 1;
            while (scan > 0 && char.IsWhiteSpace(body[scan]))
                scan--;
            if (scan >= 0 && body[scan] == ',')
                start = scan;

            return body[..start] + body[valueEnd..];
        }

        var replacement = $"{AnswerKey}: {FormatFlowValue(answer, IsMultiple(entry))}";
        return body[..keyStart] + replacement + body[valueEnd..];
    }

    // ---------------------------------------------------------------------------------------------
    // Locating the end of a value
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    ///     YamlDotNet reports a precise <c>End</c> for scalars but not for collections, whose
    ///     <c>End</c> equals their <c>Start</c>. So a scalar answers for itself and a sequence is
    ///     measured against YAML's own indentation rules.
    /// </summary>
    private static int EndOfBlockValue(string body, YamlNode value, int keyIndent)
    {
        if (value is YamlScalarNode)
            return (int)value.End.Index;

        var start = (int)value.Start.Index;
        if (start < body.Length && (body[start] == '[' || body[start] == '{'))
            return MatchDelimiter(body, start) + 1;

        // A block sequence may be indented deeper than its key, or aligned with it. Only a `- ` item
        // can legally share the key's column, which is what separates the aligned form from the next
        // sibling key.
        var valueIndent = (int)Math.Max(value.Start.Column - 1, 0);
        var end = EndOfLineExcludingTerminator(body, start);
        var cursor = EndOfLineIncludingTerminator(body, start);

        while (cursor < body.Length)
        {
            var lineEnd = EndOfLineExcludingTerminator(body, cursor);
            var indent = IndentOf(body, cursor, lineEnd);
            var blank = indent < 0;

            if (!blank && (indent < valueIndent ||
                           (indent == valueIndent && indent <= keyIndent && !IsSequenceItem(body, cursor + indent, lineEnd))))
                break;

            if (!blank)
                end = lineEnd;

            cursor = EndOfLineIncludingTerminator(body, cursor);
        }

        return end;
    }

    private static int EndOfFlowValue(string body, YamlNode value)
    {
        if (value is YamlScalarNode)
            return (int)value.End.Index;

        var start = (int)value.Start.Index;
        return MatchDelimiter(body, start) + 1;
    }

    /// <summary>
    ///     Where a new key may be appended to a block mapping: the end of its last content line.
    ///     A line belongs to the entry while it is blank or indented at least as far as the entry's
    ///     keys; trailing blank lines are given back, so the key lands against the content.
    /// </summary>
    private static int EndOfBlockEntry(string body, YamlMappingNode entry)
    {
        var keyIndent = KeyIndent(entry);
        var cursor = (int)entry.Start.Index;
        var end = EndOfLineExcludingTerminator(body, cursor);
        cursor = EndOfLineIncludingTerminator(body, cursor);

        while (cursor < body.Length)
        {
            var lineEnd = EndOfLineExcludingTerminator(body, cursor);
            var indent = IndentOf(body, cursor, lineEnd);

            if (indent >= 0 && indent < keyIndent)
                break;

            if (indent >= 0)
                end = lineEnd;

            cursor = EndOfLineIncludingTerminator(body, cursor);
        }

        return end;
    }

    /// <summary>Matches the <c>[</c> or <c>{</c> at <paramref name="open" />, ignoring quoted text.</summary>
    private static int MatchDelimiter(string body, int open)
    {
        var closer = body[open] == '[' ? ']' : '}';
        var opener = body[open];
        var depth = 0;

        for (var i = open; i < body.Length; i++)
        {
            var c = body[i];

            if (c is '\'' or '"')
            {
                i = SkipQuoted(body, i);
                continue;
            }

            if (c == opener)
                depth++;
            else if (c == closer && --depth == 0)
                return i;
        }

        return body.Length - 1;
    }

    /// <summary>Index of the closing quote of the scalar opened at <paramref name="start" />.</summary>
    private static int SkipQuoted(string body, int start)
    {
        var quote = body[start];
        for (var i = start + 1; i < body.Length; i++)
        {
            if (quote == '"' && body[i] == '\\')
            {
                i++;
                continue;
            }

            if (body[i] != quote)
                continue;

            // '' inside a single-quoted scalar is an escaped quote, not the end.
            if (quote == '\'' && i + 1 < body.Length && body[i + 1] == '\'')
            {
                i++;
                continue;
            }

            return i;
        }

        return body.Length - 1;
    }

    // ---------------------------------------------------------------------------------------------
    // Rendering an answer
    // ---------------------------------------------------------------------------------------------

    private static string FormatBlockAnswer(IReadOnlyList<string> answer, bool multiple, int indent, string newline)
    {
        // A single entry stays a scalar only when the question is single-select. More than one entry
        // is always written as a list, even if `multiple` is false: dropping the extras would lose the
        // user's answer, and the lint rule then reports the mismatch honestly.
        if (!multiple && answer.Count == 1)
            return $"{AnswerKey}: {FormatScalar(answer[0])}";

        var item = new string(' ', indent + 2) + "- ";
        return $"{AnswerKey}:" + string.Concat(answer.Select(entry => newline + item + FormatScalar(entry)));
    }

    private static string FormatFlowValue(IReadOnlyList<string> answer, bool multiple)
    {
        if (!multiple && answer.Count == 1)
            return FormatScalar(answer[0]);

        return "[" + string.Join(", ", answer.Select(FormatScalar)) + "]";
    }

    /// <summary>
    ///     A YAML scalar for <paramref name="value" />, plain where that is unambiguous and
    ///     double-quoted otherwise. Free text is whatever the user typed, so this has to survive
    ///     punctuation, newlines and words YAML would otherwise read as booleans or numbers.
    /// </summary>
    private static string FormatScalar(string value)
    {
        if (IsPlainSafe(value))
            return value;

        var quoted = new StringBuilder(value.Length + 2);
        quoted.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': quoted.Append("\\\""); break;
                case '\\': quoted.Append("\\\\"); break;
                case '\n': quoted.Append("\\n"); break;
                case '\r': quoted.Append("\\r"); break;
                case '\t': quoted.Append("\\t"); break;
                default:
                    if (char.IsControl(c))
                        quoted.Append(CultureInfo.InvariantCulture, $"\\x{(int)c:x2}");
                    else
                        quoted.Append(c);
                    break;
            }
        }

        quoted.Append('"');
        return quoted.ToString();
    }

    /// <summary>
    ///     Deliberately narrow: anything outside this set is quoted rather than reasoned about. The
    ///     excluded punctuation is what a flow sequence and a mapping key would otherwise capture.
    /// </summary>
    private static readonly string[] ReservedWords =
        ["null", "~", "true", "false", "yes", "no", "on", "off"];

    private static bool IsPlainSafe(string value)
    {
        if (value.Length == 0 || value.Trim() != value)
            return false;

        if (ReservedWords.Contains(value, StringComparer.OrdinalIgnoreCase))
            return false;

        // A number-looking scalar would come back as a number, not the string the user typed.
        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
            return false;

        if (!char.IsLetterOrDigit(value[0]))
            return false;

        foreach (var c in value)
        {
            if (!char.IsLetterOrDigit(c) && !" _./-".Contains(c))
                return false;
        }

        return true;
    }

    // ---------------------------------------------------------------------------------------------
    // Line and fence mechanics
    // ---------------------------------------------------------------------------------------------

    private static int KeyIndent(YamlMappingNode entry) =>
        entry.Children.Count > 0 ? (int)Math.Max(entry.Children.First().Key.Start.Column - 1, 0) : 0;

    private static string DetectNewline(string body) => body.Contains("\r\n") ? "\r\n" : "\n";

    private static int StartOfLine(string body, int index)
    {
        var start = body.LastIndexOf('\n', Math.Max(index - 1, 0));
        return start < 0 ? 0 : start + 1;
    }

    private static int EndOfLineExcludingTerminator(string body, int index)
    {
        var lf = body.IndexOf('\n', index);
        var end = lf < 0 ? body.Length : lf;
        return end > index && body[end - 1] == '\r' ? end - 1 : end;
    }

    private static int EndOfLineIncludingTerminator(string body, int index)
    {
        var lf = body.IndexOf('\n', index);
        return lf < 0 ? body.Length : lf + 1;
    }

    /// <summary>Leading spaces on the line at <paramref name="start" />, or -1 when it is blank.</summary>
    private static int IndentOf(string body, int start, int lineEnd)
    {
        var i = start;
        while (i < lineEnd && body[i] == ' ')
            i++;

        return i >= lineEnd ? -1 : i - start;
    }

    private static bool IsSequenceItem(string body, int index, int lineEnd) =>
        index < lineEnd && body[index] == '-' &&
        (index + 1 >= lineEnd || body[index + 1] == ' ' || body[index + 1] == '\t');

    private static string Dedent(string body, int indent)
    {
        if (indent == 0)
            return body;

        return string.Join('\n', body.Split('\n').Select(line =>
        {
            var strip = 0;
            while (strip < indent && strip < line.Length && line[strip] == ' ')
                strip++;

            return line[strip..];
        }));
    }

    private static string Reindent(string body, int indent)
    {
        if (indent == 0)
            return body;

        var pad = new string(' ', indent);
        return string.Join('\n', body.Split('\n').Select(line => line.Length == 0 ? line : pad + line));
    }

    /// <summary>
    ///     Fence tracking follows CommonMark, exactly as <c>QuestionBlockParser</c> and
    ///     <c>questionsSource.ts</c> do and for the same reason: a <c>questions</c> fence written
    ///     inside a longer fence is documentation, not a question. A fence opened with N of a
    ///     delimiter is closed only by a bare run of N or more of the same delimiter.
    /// </summary>
    private static List<RawBlock> ScanBlocks(string markdown)
    {
        var blocks = new List<RawBlock>();
        if (string.IsNullOrEmpty(markdown))
            return blocks;

        var open = false;
        var openChar = '\0';
        var openLength = 0;
        var openIndent = 0;
        var isQuestions = false;
        var bodyStart = 0;

        var pos = 0;
        while (pos <= markdown.Length)
        {
            var lineEnd = EndOfLineExcludingTerminator(markdown, pos);
            var next = EndOfLineIncludingTerminator(markdown, pos);
            var line = markdown[pos..lineEnd];
            var fence = MatchFence(line);

            if (!open)
            {
                if (fence is { } opening)
                {
                    open = true;
                    openChar = opening.Delimiter;
                    openLength = opening.Length;
                    openIndent = opening.Indent;
                    isQuestions = IsQuestionsInfo(opening.Info);
                    bodyStart = next;
                }
            }
            else if (fence is { } closing && closing.Delimiter == openChar &&
                     closing.Length >= openLength && closing.Info.Length == 0)
            {
                if (isQuestions)
                    blocks.Add(new RawBlock(blocks.Count, bodyStart, pos, markdown[bodyStart..pos], openIndent));

                open = false;
            }

            if (next == pos)
                break;

            pos = next;
        }

        // An unterminated fence runs to the end of the document (CommonMark).
        if (open && isQuestions)
            blocks.Add(new RawBlock(blocks.Count, bodyStart, markdown.Length, markdown[bodyStart..], openIndent));

        return blocks;
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

    private readonly record struct Fence(char Delimiter, int Length, int Indent, string Info);

    private readonly record struct RawBlock(int Index, int BodyStart, int BodyEnd, string Body, int Indent);
}
