using Ivy.Tendril.Commands;

namespace Ivy.Tendril.Services.Plans;

/// <summary>
///     A plan that may already cover the work an incoming plan describes. <c>State</c> is returned
///     verbatim (never filtered) so the caller can apply CreatePlan's state-aware duplicate table,
///     which has a distinct row for every state including <c>Skipped</c> and <c>Icebox</c>.
/// </summary>
public sealed record DuplicateCandidate(string FolderName, string Title, string State);

/// <summary>
///     Finds existing plans whose titles overlap a candidate title, so duplicate detection can run
///     at plan creation and at revision write instead of only at job start.
///     <para>
///         Plans are created in concurrent batches: four plans delivering one pre-commit hook block
///         landed within 17 minutes, because each CreatePlan job had already started (and so could
///         not see its siblings) before the others existed. Running this finder at the two CLI
///         surfaces that execute *after* research (<c>plan create</c> and <c>plan write-revision</c>)
///         is what catches the same-batch case.
///     </para>
///     <para>
///         Matching is significant-word overlap, not the whole-title substring that
///         <c>plan list --search</c> uses: the four sibling titles share no common substring, so a
///         substring test finds none of them.
///     </para>
/// </summary>
public static class DuplicateCandidateFinder
{
    /// <summary>
    ///     Words carrying no topical signal. Shared stopwords never contribute to a match.
    /// </summary>
    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        "the", "a", "an", "and", "or", "of", "to", "in", "for", "with",
        "on", "at", "is", "by", "from", "that", "this", "it", "as", "be"
    };

    /// <summary>
    ///     Shortest token length that counts toward the two-token overlap threshold. Shorter tokens
    ///     ("fmt", "pre", "ci") are too common to discriminate on their own.
    /// </summary>
    private const int MinimumTokenLength = 4;

    /// <summary>
    ///     Finds plans in <paramref name="plansDirectory" /> that may duplicate
    ///     <paramref name="title" />, restricted to <paramref name="project" />.
    ///     <para>
    ///         Never throws. A missing plans directory, an unreadable or malformed <c>plan.yaml</c>,
    ///         and a non-plan directory are all skipped: candidate discovery is advisory, and must
    ///         not be able to fail plan creation.
    ///     </para>
    /// </summary>
    /// <param name="plansDirectory">Directory holding the plan folders.</param>
    /// <param name="title">Title of the incoming plan to match against.</param>
    /// <param name="project">
    ///     Project to restrict the search to. A Rusty-Framework title must not match an
    ///     Ivy-Tendril plan, however similar the words.
    /// </param>
    /// <param name="excludeFolderName">
    ///     Folder name to omit, so a plan never matches itself when the finder runs after the plan
    ///     has been written to disk.
    /// </param>
    public static IReadOnlyList<DuplicateCandidate> Find(
        string plansDirectory,
        string title,
        string project,
        string? excludeFolderName = null)
    {
        var queryTokens = Tokenize(title);
        if (queryTokens.Count == 0)
            return [];

        List<PlanListCommand.PlanListEntry> plans;
        try
        {
            // Reuse the CLI's own scan rather than forking YAML parsing. It already applies the
            // project filter and swallows per-directory read errors.
            plans = PlanListCommand.ScanPlans(plansDirectory, new PlanListSettings { Project = project });
        }
        catch
        {
            return [];
        }

        var candidates = new List<DuplicateCandidate>();

        foreach (var plan in plans)
        {
            if (!string.IsNullOrEmpty(excludeFolderName) &&
                plan.FolderName.Equals(excludeFolderName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.IsNullOrWhiteSpace(plan.Title))
                continue;

            if (!IsMatch(queryTokens, Tokenize(plan.Title)))
                continue;

            candidates.Add(new DuplicateCandidate(plan.FolderName, plan.Title, plan.State));
        }

        return candidates;
    }

    /// <summary>
    ///     Renders candidates in the <c>DuplicateCandidates:</c> block format the CreatePlan
    ///     firmware documents: a header line, then one <c>folderName|title|state</c> line each.
    ///     Returns an empty string for an empty list, since the firmware branches on the header's
    ///     absence.
    /// </summary>
    public static string FormatBlock(IReadOnlyList<DuplicateCandidate> candidates)
    {
        if (candidates.Count == 0)
            return "";

        var lines = new List<string> { "DuplicateCandidates:" };
        lines.AddRange(candidates.Select(c => $"{c.FolderName}|{c.Title}|{c.State}"));
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    ///     Two titles match when they share at least two significant tokens of
    ///     <see cref="MinimumTokenLength" /> characters or more, or a single plan id token such as
    ///     <c>00042</c> (a title naming another plan's id is almost always about that plan).
    /// </summary>
    private static bool IsMatch(HashSet<string> queryTokens, HashSet<string> otherTokens)
    {
        var significantOverlap = 0;

        foreach (var token in queryTokens)
        {
            if (!otherTokens.Contains(token))
                continue;

            if (IsPlanId(token))
                return true;

            if (token.Length >= MinimumTokenLength && ++significantOverlap >= 2)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     True for an all-digit token long enough to be a plan id reference (<c>00042</c>), as
    ///     opposed to an incidental small number.
    /// </summary>
    private static bool IsPlanId(string token) =>
        token.Length >= 4 && token.All(char.IsAsciiDigit);

    /// <summary>
    ///     Lowercases, splits on every non-alphanumeric character, and drops stopwords.
    /// </summary>
    private static HashSet<string> Tokenize(string? title)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(title))
            return tokens;

        var lowered = title.ToLowerInvariant();
        var start = -1;

        for (var i = 0; i <= lowered.Length; i++)
        {
            var isWordChar = i < lowered.Length && char.IsLetterOrDigit(lowered[i]);

            if (isWordChar)
            {
                if (start < 0) start = i;
                continue;
            }

            if (start < 0) continue;

            var token = lowered[start..i];
            start = -1;
            if (!Stopwords.Contains(token))
                tokens.Add(token);
        }

        return tokens;
    }
}
