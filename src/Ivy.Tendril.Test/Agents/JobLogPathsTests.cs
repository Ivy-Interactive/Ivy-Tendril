using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Test.Agents;

public class JobLogPathsTests : IDisposable
{
    private readonly string _tendrilHome;

    public JobLogPathsTests()
    {
        _tendrilHome = Path.Combine(Path.GetTempPath(), $"tendril-joblogpaths-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tendrilHome);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tendrilHome))
                Directory.Delete(_tendrilHome, true);
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    [Fact]
    public void Stem_WithPlan_IncludesPlanId()
    {
        var job = new JobItem { Id = "00458", Type = "ExecutePlan", PlanFile = "00044-PersistJobOutput" };

        Assert.Equal("00458-00044-ExecutePlan", JobLogPaths.Stem(job));
    }

    [Fact]
    public void Stem_WithoutPlan_OmitsPlanId()
    {
        // CreatePlan jobs carry the task description in PlanFile, not a plan folder name.
        var job = new JobItem { Id = "00459", Type = "CreatePlan", PlanFile = "Add a logout button" };

        Assert.Equal("00459-CreatePlan", JobLogPaths.Stem(job));
    }

    [Fact]
    public void Stem_WithEmptyPlanFile_OmitsPlanId()
    {
        var job = new JobItem { Id = "00460", Type = "SyncRepo" };

        Assert.Equal("00460-SyncRepo", JobLogPaths.Stem(job));
    }

    [Fact]
    public void Stem_IsStableWhenAJobLaterLearnsItsPlanId()
    {
        // A CreatePlan job allocates its plan id mid-run. The stem must not shift, or the eventwire file
        // written at the start of the run would be orphaned by the log written at the end.
        var job = new JobItem { Id = "00459", Type = "CreatePlan" };
        var before = JobLogPaths.Stem(job);

        job.AllocatedPlanId = "00075";
        job.ReportedPlanId = "00075";

        Assert.Equal(before, JobLogPaths.Stem(job));
    }

    [Fact]
    public void Stem_IsStableWhenCreatePlanCompletionRewritesPlanFile()
    {
        // JobCompletionHandler.VerifyCreatePlanResult replaces a CreatePlan job's PlanFile (the task
        // description) with the folder it produced. PersistJob then rewrites the eventwire file — if the
        // stem tracked PlanFile it would land under a different name than the .md/.prompt.md/.raw.jsonl
        // siblings that were named at launch.
        var job = new JobItem { Id = "00459", Type = "CreatePlan", PlanFile = "Add a logout button" };
        var before = JobLogPaths.Stem(job);

        job.PlanFile = "00075-AddLogoutButton";

        Assert.Equal(before, JobLogPaths.Stem(job));
        Assert.Equal("00459-CreatePlan", JobLogPaths.Stem(job));
    }

    [Fact]
    public void Stem_CreatePlanWhoseDescriptionLooksLikeAPlanFolder_StillHasNoPlanId()
    {
        // The description is free text and could begin with five digits and a dash.
        var job = new JobItem { Id = "00459", Type = "CreatePlan", PlanFile = "12345-looks-like-a-folder" };

        Assert.Equal("00459-CreatePlan", JobLogPaths.Stem(job));
    }

    [Fact]
    public void AllPaths_ShareTheStemAndDifferOnlyBySuffix()
    {
        var job = new JobItem { Id = "00458", Type = "ExecutePlan", PlanFile = "00044-Demo" };
        var jobs = Path.Combine(_tendrilHome, "Jobs");

        Assert.Equal(Path.Combine(jobs, "00458-00044-ExecutePlan.md"), JobLogPaths.Log(_tendrilHome, job));
        Assert.Equal(Path.Combine(jobs, "00458-00044-ExecutePlan.prompt.md"), JobLogPaths.Prompt(_tendrilHome, job));
        Assert.Equal(Path.Combine(jobs, "00458-00044-ExecutePlan.raw.jsonl"), JobLogPaths.Raw(_tendrilHome, job));
        Assert.Equal(Path.Combine(jobs, "00458-00044-ExecutePlan.eventwire.jsonl"), JobLogPaths.EventWire(_tendrilHome, job));
    }

    [Fact]
    public void Raw_MatchesWhatJobItemDerivesFromLogFilePath()
    {
        // JobItem.OpenLogWriters derives the raw path via Path.ChangeExtension on LogFilePath.
        var job = new JobItem { Id = "00458", Type = "ExecutePlan", PlanFile = "00044-Demo" };
        var derived = Path.ChangeExtension(JobLogPaths.Log(_tendrilHome, job), ".raw.jsonl");

        Assert.Equal(JobLogPaths.Raw(_tendrilHome, job), derived);
    }

    [Fact]
    public void PlanIdFromFolderName_ReadsTheFiveDigitPrefix()
    {
        Assert.Equal("00044", JobLogPaths.PlanIdFromFolderName("00044-Demo"));
        Assert.Null(JobLogPaths.PlanIdFromFolderName("Demo"));
        Assert.Null(JobLogPaths.PlanIdFromFolderName("123-Demo"));
        Assert.Null(JobLogPaths.PlanIdFromFolderName(""));
        Assert.Null(JobLogPaths.PlanIdFromFolderName(null));
    }

    [Fact]
    public void AllForJobId_ReturnsEveryArtifactAndNothingFromOtherJobs()
    {
        var jobs = JobLogPaths.EnsureJobsDir(_tendrilHome);
        foreach (var name in new[]
                 {
                     "00458-00044-ExecutePlan.md", "00458-00044-ExecutePlan.prompt.md",
                     "00458-00044-ExecutePlan.raw.jsonl", "00458-00044-ExecutePlan.eventwire.jsonl",
                     "00459-00044-CreatePr.md"
                 })
            File.WriteAllText(Path.Combine(jobs, name), "x");

        var found = JobLogPaths.AllForJobId(_tendrilHome, "00458")
            .Select(f => Path.GetFileName(f)!).ToArray();

        Assert.Equal(4, found.Length);
        Assert.DoesNotContain("00459-00044-CreatePr.md", found);
    }

    [Fact]
    public void AllForJobId_MissingJobsDir_ReturnsEmpty()
    {
        Assert.Empty(JobLogPaths.AllForJobId(_tendrilHome, "00458"));
    }

    [Fact]
    public void ResolvingAPath_DoesNotCreateTheJobsDirectory()
    {
        // Read paths (the Job Debug sheet, eventwire hydration) resolve all four artifacts. Resolution
        // must stay pure — only writers call EnsureJobsDir.
        var job = new JobItem { Id = "00458", Type = "ExecutePlan", PlanFile = "00044-Demo" };

        _ = JobLogPaths.Log(_tendrilHome, job);
        _ = JobLogPaths.Prompt(_tendrilHome, job);
        _ = JobLogPaths.Raw(_tendrilHome, job);
        _ = JobLogPaths.EventWire(_tendrilHome, job);
        _ = JobLogPaths.JobsDir(_tendrilHome);

        Assert.False(Directory.Exists(Path.Combine(_tendrilHome, "Jobs")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void JobsDir_WithNoTendrilHome_Throws(string tendrilHome)
    {
        // Path.Combine("", "Jobs") silently yields the relative path "Jobs", which would create a stray
        // directory in the process working directory.
        Assert.Throws<ArgumentException>(() => JobLogPaths.JobsDir(tendrilHome));
    }

    [Fact]
    public void EnsureJobsDir_CreatesIt()
    {
        Assert.True(Directory.Exists(JobLogPaths.EnsureJobsDir(_tendrilHome)));
    }

    [Fact]
    public void LogsForPlanId_ReturnsJobLogsOldestFirstAndExcludesPrompts()
    {
        var jobs = JobLogPaths.EnsureJobsDir(_tendrilHome);
        foreach (var name in new[]
                 {
                     "00011-00044-ExecutePlan.md", "00010-00044-ExpandPlan.md",
                     "00011-00044-ExecutePlan.prompt.md", "00012-00099-ExecutePlan.md"
                 })
            File.WriteAllText(Path.Combine(jobs, name), "x");

        var found = JobLogPaths.LogsForPlanId(_tendrilHome, "00044")
            .Select(f => Path.GetFileName(f)!).ToArray();

        Assert.Equal(["00010-00044-ExpandPlan.md", "00011-00044-ExecutePlan.md"], found);
    }
}
