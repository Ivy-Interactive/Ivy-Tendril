using Ivy.Tendril.Commands;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Plans;

namespace Ivy.Tendril.Test.Services;

[Collection("TendrilHome")]
public class DuplicateCandidateFinderTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("tendril-duplicate-candidates");
    private readonly string _plansDir;

    public DuplicateCandidateFinderTests()
    {
        _plansDir = Path.Combine(_tempDir.Path, "Plans");
        Directory.CreateDirectory(_plansDir);
    }

    public void Dispose() => _tempDir.Dispose();

    private string SeedPlan(string folderName, string title, string project, string state = "Draft")
    {
        var planDir = Path.Combine(_plansDir, folderName);
        Directory.CreateDirectory(planDir);

        var plan = new PlanYaml
        {
            State = state,
            Project = project,
            Title = title,
            Level = "Bug",
            Created = new DateTime(2026, 8, 1, 20, 0, 0, DateTimeKind.Utc),
            Updated = new DateTime(2026, 8, 1, 20, 0, 0, DateTimeKind.Utc)
        };

        File.WriteAllText(Path.Combine(planDir, "plan.yaml"), YamlHelper.Serializer.Serialize(plan));
        return planDir;
    }

    // The real titles of the four plans that collapsed onto one deliverable within 17 minutes.
    private const string Title00042 = "Remove Dead Dotnet Format Staged Glob and Enforce Cargo Fmt on Pre-Commit";
    private const string Title00061 = "Deliver the Cargo Fmt Pre-Commit Hook That Plan 00042 Could Not Write";
    private const string Title00063 = "Add the Missing Cargo Fmt Pre-Commit Guard From Plan 00042";
    private const string Title00064 = "Cover the Pre-Commit Hook With a CI Harness and Land the Missing Rustfmt Block";
    private const string Title00065 = "Deliver the Blocked Cargo Fmt Pre-Commit Hook and Correct Plan 00042's Motivation";

    private void SeedTheFourSiblings()
    {
        SeedPlan("00042-RemoveDeadDotnetFormatStagedGlobAndEnforceCargoFmtOnPreCommi", Title00042, "Rusty-Framework", "Completed");
        SeedPlan("00061-MakeTheCIFormatCheckGreenByRemovingTheDuplicateBlankLineInWi", Title00061, "Rusty-Framework", "Completed");
        SeedPlan("00063-AddTheMissingCargoFmtPreCommitGuardFromPlan00042", Title00063, "Rusty-Framework", "Completed");
        SeedPlan("00064-CoverThePreCommitHookWithACIHarnessAndLandTheMissingRustfmtB", Title00064, "Rusty-Framework", "Completed");
    }

    /// <summary>
    ///     The regression this plan exists for: querying with 00065's title must surface all four
    ///     plans already on disk when 00065 was finalized.
    /// </summary>
    [Fact]
    public void Find_TheFourRealSiblings_ReturnsAllOfThem()
    {
        SeedTheFourSiblings();

        var candidates = DuplicateCandidateFinder.Find(_plansDir, Title00065, "Rusty-Framework");

        Assert.Equal(4, candidates.Count);
        var folders = candidates.Select(c => c.FolderName.Split('-')[0]).OrderBy(x => x).ToArray();
        Assert.Equal(["00042", "00061", "00063", "00064"], folders);
    }

    /// <summary>
    ///     The mechanism this replaces must be shown not to work, otherwise the finder's green run
    ///     proves nothing. <c>plan list --search</c> is a whole-title substring match, and the four
    ///     sibling titles share no common substring: searching for any one of them finds only itself.
    /// </summary>
    [Fact]
    public void ScanPlansSubstringSearch_TheFourRealSiblings_FindsNoneOfThem()
    {
        SeedTheFourSiblings();

        var bySubstring = PlanListCommand.ScanPlans(
            _plansDir,
            new PlanListSettings { Project = "Rusty-Framework", Search = Title00065 });

        Assert.Empty(bySubstring);
    }

    [Fact]
    public void Find_DifferentProject_IsExcludedEvenOnAnIdenticalTitle()
    {
        SeedPlan("00100-SameTitle", Title00065, "Ivy-Tendril", "Completed");

        var sameProject = DuplicateCandidateFinder.Find(_plansDir, Title00065, "Ivy-Tendril");
        var otherProject = DuplicateCandidateFinder.Find(_plansDir, Title00065, "Rusty-Framework");

        Assert.Single(sameProject);
        Assert.Empty(otherProject);
    }

    [Fact]
    public void Find_ExcludeFolderName_RemovesThePlansOwnFolder()
    {
        SeedPlan("00065-DeliverTheBlockedCargoFmtPreCommitHook", Title00065, "Rusty-Framework", "Draft");
        SeedPlan("00063-AddTheMissingCargoFmtPreCommitGuardFromPlan00042", Title00063, "Rusty-Framework", "Completed");

        var withoutExclude = DuplicateCandidateFinder.Find(_plansDir, Title00065, "Rusty-Framework");
        var withExclude = DuplicateCandidateFinder.Find(
            _plansDir, Title00065, "Rusty-Framework", "00065-DeliverTheBlockedCargoFmtPreCommitHook");

        Assert.Equal(2, withoutExclude.Count);
        Assert.Single(withExclude);
        Assert.StartsWith("00063", withExclude[0].FolderName);
    }

    [Fact]
    public void Find_OnlyStopwordsAndShortTokensShared_DoesNotMatch()
    {
        SeedPlan("00200-AddAThingToTheDocs", "Add a Thing to the Docs", "Ivy-Tendril");

        var candidates = DuplicateCandidateFinder.Find(_plansDir, "Fix the Thing in the App", "Ivy-Tendril");

        Assert.Empty(candidates);
    }

    [Fact]
    public void Find_SinglePlanIdTokenShared_MatchesOnItsOwn()
    {
        SeedPlan("00300-SomethingEntirelyUnrelatedAboutPlan00042", "Something Entirely Unrelated About Plan 00042", "Ivy-Tendril");

        var candidates = DuplicateCandidateFinder.Find(_plansDir, "Revisit Plan 00042 Whenever Possible", "Ivy-Tendril");

        Assert.Single(candidates);
        Assert.StartsWith("00300", candidates[0].FolderName);
    }

    [Fact]
    public void Find_MalformedEmptyAndNonPlanDirectories_AreSkippedWithoutThrowing()
    {
        // Malformed YAML.
        var malformed = Path.Combine(_plansDir, "00400-Malformed");
        Directory.CreateDirectory(malformed);
        File.WriteAllText(Path.Combine(malformed, "plan.yaml"), "state: [unclosed\n\ttitle: \"Cargo Fmt Pre-Commit Hook\"");

        // Empty plan.yaml.
        var empty = Path.Combine(_plansDir, "00401-Empty");
        Directory.CreateDirectory(empty);
        File.WriteAllText(Path.Combine(empty, "plan.yaml"), "");

        // A directory with no plan.yaml at all.
        Directory.CreateDirectory(Path.Combine(_plansDir, "00402-NoYaml"));

        // A directory whose name has no numeric plan-id prefix.
        var nonPlan = Path.Combine(_plansDir, "NotAPlanFolder");
        Directory.CreateDirectory(nonPlan);
        File.WriteAllText(Path.Combine(nonPlan, "plan.yaml"), "state: Draft\nproject: Rusty-Framework\ntitle: Cargo Fmt Pre-Commit Hook\n");

        // One good plan, to prove the scan kept going past all of the above.
        SeedPlan("00403-Good", Title00063, "Rusty-Framework", "Completed");

        var candidates = DuplicateCandidateFinder.Find(_plansDir, Title00065, "Rusty-Framework");

        Assert.Single(candidates);
        Assert.StartsWith("00403", candidates[0].FolderName);
    }

    [Fact]
    public void Find_MissingPlansDirectory_ReturnsEmptyWithoutThrowing()
    {
        var candidates = DuplicateCandidateFinder.Find(
            Path.Combine(_tempDir.Path, "does-not-exist"), Title00065, "Rusty-Framework");

        Assert.Empty(candidates);
    }

    [Fact]
    public void Find_ReturnsStateVerbatimForEveryState()
    {
        SeedPlan("00500-Completed", Title00063, "Rusty-Framework", "Completed");
        SeedPlan("00501-Failed", Title00063, "Rusty-Framework", "Failed");
        SeedPlan("00502-Skipped", Title00063, "Rusty-Framework", "Skipped");
        SeedPlan("00503-Icebox", Title00063, "Rusty-Framework", "Icebox");

        var candidates = DuplicateCandidateFinder.Find(_plansDir, Title00065, "Rusty-Framework");

        var states = candidates.Select(c => c.State).OrderBy(s => s).ToArray();
        Assert.Equal(["Completed", "Failed", "Icebox", "Skipped"], states);
    }

    [Fact]
    public void Find_EmptyOrStopwordOnlyTitle_ReturnsEmpty()
    {
        SeedPlan("00600-Something", Title00063, "Rusty-Framework", "Completed");

        Assert.Empty(DuplicateCandidateFinder.Find(_plansDir, "", "Rusty-Framework"));
        Assert.Empty(DuplicateCandidateFinder.Find(_plansDir, "   ", "Rusty-Framework"));
        Assert.Empty(DuplicateCandidateFinder.Find(_plansDir, "the and of to", "Rusty-Framework"));
    }

    [Fact]
    public void FormatBlock_EmptyList_ReturnsEmptyString()
    {
        Assert.Equal("", DuplicateCandidateFinder.FormatBlock([]));
    }

    [Fact]
    public void FormatBlock_Candidates_UsesFolderNamePipeTitlePipeState()
    {
        var block = DuplicateCandidateFinder.FormatBlock(
        [
            new DuplicateCandidate("00063-AddTheMissingCargoFmtPreCommitGuardFromPlan00042", Title00063, "Completed"),
            new DuplicateCandidate("00064-CoverThePreCommitHookWithACIHarnessAndLandTheMissingRustfmtB", Title00064, "Failed")
        ]);

        var lines = block.Split(Environment.NewLine);
        Assert.Equal("DuplicateCandidates:", lines[0]);
        Assert.Equal($"00063-AddTheMissingCargoFmtPreCommitGuardFromPlan00042|{Title00063}|Completed", lines[1]);
        Assert.Equal($"00064-CoverThePreCommitHookWithACIHarnessAndLandTheMissingRustfmtB|{Title00064}|Failed", lines[2]);
        Assert.Equal(3, lines.Length);
    }
}
