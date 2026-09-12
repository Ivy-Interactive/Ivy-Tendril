using System.Text;

namespace Ivy.Tendril.Helpers;

/// <summary>
///     Describes what one revision changed relative to the one before it, in a single line short
///     enough to carry inside a chat system event ("Solution changed (+12/-3 lines), Tests added").
///     <para>
///         Pure over two strings on purpose: the plan-edit event is reported from a CLI process that
///         has already written the revision, so the description must be derivable from the two
///         markdown bodies alone, with no plans directory and no services to mock.
///     </para>
/// </summary>
public static class PlanEditSummary
{
    private const string NoPreviousRevision = "initial revision written";
    private const string NoSectionChanges = "no section changes";

    /// <summary>The <c>##</c> sections of one revision, in document order.</summary>
    private sealed class Sections
    {
        private readonly Dictionary<string, List<string>> _bodies = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Section names in the order they appear in the document.</summary>
        public List<string> Names { get; } = [];

        public List<string> this[string name] => _bodies[name];

        public bool Contains(string name) => _bodies.ContainsKey(name);

        public void Add(string name, List<string> body)
        {
            if (_bodies.TryAdd(name, body))
                Names.Add(name);
            else
                _bodies[name] = body;
        }
    }

    /// <summary>
    ///     One line naming the <c>##</c> sections that were added, removed, renamed or changed
    ///     between <paramref name="previousRevision" /> and <paramref name="newRevision" />.
    /// </summary>
    /// <param name="previousRevision">
    ///     The revision written before this one, or null/empty when this is the plan's first.
    /// </param>
    public static string Describe(string? previousRevision, string newRevision)
    {
        if (string.IsNullOrWhiteSpace(previousRevision))
            return NoPreviousRevision;

        var before = ReadSections(previousRevision);
        var after = ReadSections(newRevision ?? string.Empty);

        var added = after.Names.Where(name => !before.Contains(name)).ToList();
        var removed = before.Names.Where(name => !after.Contains(name)).ToList();
        var renames = MatchRenames(before, after, added, removed);

        var clauses = new List<string>();

        foreach (var name in after.Names)
        {
            if (renames.TryGetValue(name, out var oldName))
            {
                clauses.Add($"{name} renamed from {oldName}");
                continue;
            }

            if (added.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                clauses.Add($"{name} added");
                continue;
            }

            var (plus, minus) = CountLineDelta(before[name], after[name]);
            if (plus > 0 || minus > 0)
                clauses.Add($"{name} changed (+{plus}/-{minus} lines)");
        }

        foreach (var name in removed)
            clauses.Add($"{name} removed");

        return clauses.Count > 0 ? string.Join(", ", clauses) : NoSectionChanges;
    }

    /// <summary>
    ///     Splits a revision into its <c>##</c> sections. A heading inside a fenced code block is body
    ///     text, not a section — plans quote markdown, and this document is itself an example.
    /// </summary>
    private static Sections ReadSections(string markdown)
    {
        var sections = new Sections();
        var current = new List<string>();
        string? currentName = null;
        string? fence = null;

        foreach (var rawLine in markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = rawLine.TrimEnd();
            var trimmed = line.TrimStart();

            if (fence != null)
            {
                if (trimmed.StartsWith(fence, StringComparison.Ordinal))
                    fence = null;
            }
            else if (trimmed.StartsWith("```", StringComparison.Ordinal) ||
                     trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                fence = new string(trimmed[0], trimmed.TakeWhile(c => c == trimmed[0]).Count());
            }
            else if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                if (currentName != null)
                    sections.Add(currentName, current);

                currentName = trimmed[3..].Trim();
                current = [];
                continue;
            }

            if (currentName != null)
                current.Add(line);
        }

        if (currentName != null)
            sections.Add(currentName, current);

        return sections;
    }

    /// <summary>
    ///     Pairs a removed section with an added one whose body is unchanged: that is a rename rather
    ///     than a deletion plus an insertion. Returns new name to old name, and strikes both out of
    ///     <paramref name="added" /> and <paramref name="removed" />.
    /// </summary>
    private static Dictionary<string, string> MatchRenames(
        Sections before,
        Sections after,
        List<string> added,
        List<string> removed)
    {
        var renames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var newName in added.ToList())
        {
            var oldName = removed.FirstOrDefault(candidate => SameBody(before[candidate], after[newName]));
            if (oldName == null) continue;

            renames[newName] = oldName;
            added.Remove(newName);
            removed.Remove(oldName);
        }

        return renames;
    }

    private static bool SameBody(List<string> before, List<string> after) =>
        Normalize(before).SequenceEqual(Normalize(after), StringComparer.Ordinal);

    /// <summary>Body lines with the blank padding around a section dropped, so it never counts.</summary>
    private static List<string> Normalize(List<string> lines) =>
        lines.SkipWhile(string.IsNullOrWhiteSpace)
            .Reverse()
            .SkipWhile(string.IsNullOrWhiteSpace)
            .Reverse()
            .ToList();

    /// <summary>
    ///     How many lines a section gained and lost, counted as a multiset difference so a block that
    ///     only moved does not read as a rewrite. Blank padding is ignored.
    /// </summary>
    private static (int Plus, int Minus) CountLineDelta(List<string> before, List<string> after)
    {
        var remaining = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in Normalize(before))
            remaining[line] = remaining.GetValueOrDefault(line) + 1;

        var plus = 0;
        foreach (var line in Normalize(after))
        {
            if (remaining.TryGetValue(line, out var count) && count > 0)
                remaining[line] = count - 1;
            else
                plus++;
        }

        return (plus, remaining.Values.Sum());
    }

    /// <summary>
    ///     A short stable key for a summary, for deduplicating an edit event that carries no revision
    ///     file name. Not a security boundary — only equality of two summaries matters.
    /// </summary>
    public static string Fingerprint(string text)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty));
        return Convert.ToHexString(hash, 0, 8);
    }
}
