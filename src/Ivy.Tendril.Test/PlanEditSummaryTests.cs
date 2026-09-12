using Ivy.Tendril.Helpers;
using Xunit;

namespace Ivy.Tendril.Test;

/// <summary>
/// The one line that a plan-edit system event carries. It is the only thing the plan's other chat
/// sessions are told about a direct edit, so a summary that says "no section changes" for a real
/// rewrite — or the reverse — is worse than no event at all (plan 00400).
/// </summary>
public class PlanEditSummaryTests
{
    /// <summary>
    /// Normalized so the assertions below hold however git checked this file out — the tests splice
    /// <c>\n</c> into it directly.
    /// </summary>
    private static readonly string Before = """
        ## Problem

        The master agent never hears about edits made from the side chat.

        ## Solution

        Report the edit to the master.

        ## Tests

        Cover the summary helper.
        """.ReplaceLineEndings("\n");

    [Fact]
    public void Describe_WhenSectionAdded_NamesIt()
    {
        var after = Before + "\n\n## Notes\n\nAdded late.";

        Assert.Equal("Notes added", PlanEditSummary.Describe(Before, after));
    }

    [Fact]
    public void Describe_WhenSectionRemoved_NamesIt()
    {
        var after = Before.Replace("\n## Tests\n\nCover the summary helper.", "");

        Assert.Equal("Tests removed", PlanEditSummary.Describe(Before, after));
    }

    [Fact]
    public void Describe_WhenBodyChanged_ReportsLineDelta()
    {
        var after = Before.Replace(
            "Report the edit to the master.",
            "Report the edit to the master.\nFan it out to the attached sessions.\nSay why it changed.");

        Assert.Equal("Solution changed (+2/-0 lines)", PlanEditSummary.Describe(Before, after));
    }

    [Fact]
    public void Describe_WhenBodyReplaced_CountsBothDirections()
    {
        var after = Before.Replace("Report the edit to the master.", "Post it to the running instance.");

        Assert.Equal("Solution changed (+1/-1 lines)", PlanEditSummary.Describe(Before, after));
    }

    [Fact]
    public void Describe_WhenNothingChanged_SaysSo()
    {
        Assert.Equal("no section changes", PlanEditSummary.Describe(Before, Before));
    }

    [Fact]
    public void Describe_WhenNoPreviousRevision_SaysInitial()
    {
        Assert.Equal("initial revision written", PlanEditSummary.Describe(null, Before));
        Assert.Equal("initial revision written", PlanEditSummary.Describe("", Before));
        Assert.Equal("initial revision written", PlanEditSummary.Describe("   \n", Before));
    }

    [Fact]
    public void Describe_ReportsEveryChangedSectionInDocumentOrder()
    {
        var after = Before
            .Replace("The master agent never hears about edits made from the side chat.", "Rewritten.")
            .Replace("Cover the summary helper.", "Cover the summary helper.\nAnd the fan-out.")
            + "\n\n## Notes\n\nLast.";

        Assert.Equal(
            "Problem changed (+1/-1 lines), Tests changed (+1/-0 lines), Notes added",
            PlanEditSummary.Describe(Before, after));
    }

    /// <summary>
    /// A retitled section with an untouched body is a rename, not a deletion plus an insertion:
    /// telling the master agent "Solution removed" would read as scope being dropped.
    /// </summary>
    [Fact]
    public void Describe_WhenSectionRenamedWithSameBody_ReportsRename()
    {
        var after = Before.Replace("## Solution", "## Proposed Solution");

        Assert.Equal("Proposed Solution renamed from Solution", PlanEditSummary.Describe(Before, after));
    }

    /// <summary>
    /// Plans quote markdown, including their own headings. A fenced <c>## Problem</c> is body text.
    /// </summary>
    [Fact]
    public void Describe_IgnoresHeadingsInsideFencedBlocks()
    {
        var withFence = """
            ## Problem

            Write a revision that looks like this:

            ```markdown
            ## Solution

            Not a section.
            ```

            ## Tests

            Cover it.
            """;

        var after = withFence.Replace("Not a section.", "Still not a section.");

        Assert.Equal("Problem changed (+1/-1 lines)", PlanEditSummary.Describe(withFence, after));
    }

    /// <summary>
    /// Trailing blank lines are how the revision writer pads a document, not an edit anyone made.
    /// </summary>
    [Fact]
    public void Describe_IgnoresBlankPaddingAroundSections()
    {
        Assert.Equal("no section changes", PlanEditSummary.Describe(Before, "\n\n" + Before + "\n\n\n"));
    }

    /// <summary>
    /// Line endings differ between a revision written on Windows and one written by an agent on
    /// Linux, and that difference must not read as every section having been rewritten.
    /// </summary>
    [Fact]
    public void Describe_IgnoresLineEndingStyle()
    {
        Assert.Equal("no section changes", PlanEditSummary.Describe(Before, Before.ReplaceLineEndings("\r\n")));
    }

    [Fact]
    public void Describe_WhenNeitherRevisionHasSections_SaysNoSectionChanges()
    {
        Assert.Equal("no section changes", PlanEditSummary.Describe("Just prose.", "Different prose."));
    }

    [Fact]
    public void Fingerprint_IsStableAndDistinguishesSummaries()
    {
        Assert.Equal(PlanEditSummary.Fingerprint("Solution changed"), PlanEditSummary.Fingerprint("Solution changed"));
        Assert.NotEqual(PlanEditSummary.Fingerprint("Solution changed"), PlanEditSummary.Fingerprint("Tests changed"));
        Assert.Equal(16, PlanEditSummary.Fingerprint("Solution changed").Length);
    }

    /// <summary>
    /// DraftActions polishes markdown before describing the edit, so a change that only normalizes
    /// punctuation or spacing (what PolishMarkdown does automatically) should not announce as a
    /// section change. This is why the call site polishes before describing.
    /// </summary>
    [Fact]
    public void Describe_IgnoresWhatPolishingWouldHaveChangedAnyway()
    {
        var config = new ConfigService(new TendrilSettings(), Path.GetTempPath());
        var original = Before;
        var typed = Before.Replace("Report the edit to the master.", "Report the edit to the master");

        // Without polishing, the diff shows as a change (period removed)
        Assert.Equal("Solution changed (+1/-1 lines)", PlanEditSummary.Describe(original, typed));

        // But polishing normalizes the punctuation, making them equivalent
        var polished = config.PolishMarkdown(typed);
        Assert.Equal("no section changes", PlanEditSummary.Describe(original, polished));
    }
}
