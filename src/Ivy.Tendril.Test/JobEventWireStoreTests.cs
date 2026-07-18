using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Test;

/// <summary>
/// The eventwire file is streamed by <see cref="JobItem"/> as events arrive (opened when
/// <see cref="JobItem.LogFilePath"/> is assigned), and read back by <see cref="JobEventWireStore"/>.
/// </summary>
public class JobEventWireStoreTests : IDisposable
{
    private readonly string _tendrilHome;

    public JobEventWireStoreTests()
    {
        _tendrilHome = Path.Combine(Path.GetTempPath(), $"tendril-eventwire-test-{Guid.NewGuid()}");
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

    /// <summary>Starts a job streaming to its artifacts, exactly as JobLauncher does at launch.</summary>
    private JobItem StartJob(string id, string type, string planFile = "")
    {
        var job = new JobItem { Id = id, Type = type, PlanFile = planFile };
        JobLogPaths.EnsureJobsDir(_tendrilHome);
        job.LogFilePath = JobLogPaths.Log(_tendrilHome, job);
        return job;
    }

    [Fact]
    public void EventsAreStreamedToDiskAsTheyArrive_NotAtCompletion()
    {
        var job = StartJob("00458", "ExecutePlan", "00044-Demo");

        job.EnqueueSystemOutput("first");
        job.EnqueueSystemOutput("second");

        // No completion, no flush, no explicit persist: the job is still "running".
        var path = JobLogPaths.EventWire(_tendrilHome, job);
        Assert.True(File.Exists(path), "a crashed job must still leave its eventwire on disk");
        Assert.Equal(2, JobEventWireStore.ReadAllLinesShared(path).Count);

        job.CloseLogWriters();
    }

    [Fact]
    public void StreamedEvents_RoundTripThroughRead_InOrder()
    {
        var job = StartJob("00458", "ExecutePlan", "00044-Demo");
        job.EnqueueSystemOutput("line-1");
        job.EnqueueSystemOutput("line-2");
        job.EnqueueSystemOutput("line-3");
        job.CloseLogWriters();

        var result = JobEventWireStore.Read(_tendrilHome, job);

        Assert.NotNull(result);
        var texts = result!.Select(ExtractText).ToArray();
        Assert.Equal(["line-1", "line-2", "line-3"], texts);
    }

    [Fact]
    public void EventWire_UsesTheJobLogStemForItsFileName()
    {
        var job = StartJob("00458", "ExecutePlan", "00044-Demo");
        job.EnqueueSystemOutput("x");
        job.CloseLogWriters();

        Assert.True(File.Exists(
            Path.Combine(_tendrilHome, "Jobs", "00458-00044-ExecutePlan.eventwire.jsonl")));
    }

    [Fact]
    public void EventWire_KeepsEverythingEvenWhenTheInMemoryQueueIsCapped()
    {
        var job = StartJob("00458", "ExecutePlan", "00044-Demo");
        const int overflow = JobItem.MaxOutputLines + 25;
        for (var i = 0; i < overflow; i++)
            job.EnqueueSystemOutput($"line-{i}");
        job.CloseLogWriters();

        // The queue drops the oldest; the file keeps them all.
        Assert.Equal(JobItem.MaxOutputLines, job.OutputLines.Count);
        Assert.Equal(overflow, File.ReadAllLines(JobLogPaths.EventWire(_tendrilHome, job)).Length);
    }

    [Fact]
    public void Read_TruncatesToTheTailSoHydrationCannotBlowUpMemory()
    {
        var job = StartJob("00458", "ExecutePlan", "00044-Demo");
        const int overflow = JobItem.MaxOutputLines + 25;
        for (var i = 0; i < overflow; i++)
            job.EnqueueSystemOutput($"line-{i}");
        job.CloseLogWriters();

        var result = JobEventWireStore.Read(_tendrilHome, job);

        Assert.NotNull(result);
        Assert.Equal(JobItem.MaxOutputLines, result!.Count);
        Assert.Equal($"line-{overflow - 1}", ExtractText(result.Last()));
    }

    [Fact]
    public void Read_MissingFile_ReturnsNull()
    {
        var job = new JobItem { Id = "00099", Type = "CreatePlan" };

        Assert.Null(JobEventWireStore.Read(_tendrilHome, job));
    }

    [Fact]
    public void Read_FallsBackToThePreUpgradeFlatFileName()
    {
        // Jobs that finished before the artifacts moved to a stem-based name wrote <jobId>.eventwire.jsonl.
        // Without this fallback, every historical job's output pane goes blank after an upgrade.
        var job = new JobItem { Id = "00458", Type = "ExecutePlan", PlanFile = "00044-Demo" };
        var jobsDir = JobLogPaths.EnsureJobsDir(_tendrilHome);
        File.WriteAllText(Path.Combine(jobsDir, "00458.eventwire.jsonl"), "legacy-line\n");

        var result = JobEventWireStore.Read(_tendrilHome, job);

        Assert.NotNull(result);
        Assert.Equal("legacy-line", Assert.Single(result!));
    }

    [Fact]
    public void Read_PrefersTheCurrentFileNameOverTheLegacyOne()
    {
        var job = new JobItem { Id = "00458", Type = "ExecutePlan", PlanFile = "00044-Demo" };
        var jobsDir = JobLogPaths.EnsureJobsDir(_tendrilHome);
        File.WriteAllText(Path.Combine(jobsDir, "00458.eventwire.jsonl"), "legacy-line\n");
        File.WriteAllText(Path.Combine(jobsDir, "00458-00044-ExecutePlan.eventwire.jsonl"), "current-line\n");

        var result = JobEventWireStore.Read(_tendrilHome, job);

        Assert.Equal("current-line", Assert.Single(result!));
    }

    [Fact]
    public void CompletionEventsReachTheEventWire_AfterTheRawTranscriptIsClosed()
    {
        // JobService.CompleteJob closes the raw CLI transcript, then HandleCompletion enqueues Tendril's own
        // events ("[Tendril] ...", "[hook:...]"). Those must still land in the eventwire file.
        var job = StartJob("00458", "ExecutePlan", "00044-Demo");
        job.EnqueueSystemOutput("during-run");

        job.CloseRawLog();
        job.EnqueueSystemOutput("[Tendril] summary");
        job.EnqueueSystemOutput("[hook:After Hook] ran");
        job.CloseLogWriters();

        var eventWire = JobEventWireStore.ReadAllLinesShared(JobLogPaths.EventWire(_tendrilHome, job));
        Assert.Equal(3, eventWire.Count);

        // ...and must not pollute the raw log, which is by definition the unparsed agent CLI output.
        var raw = JobEventWireStore.ReadAllLinesShared(JobLogPaths.Raw(_tendrilHome, job));
        Assert.Single(raw);
        Assert.Equal("during-run", raw[0]);
    }

    [Fact]
    public void Read_WorksWhileTheJobIsStillWritingToTheFile()
    {
        // File.ReadAllLines requests FileShare.Read, which denies the live appender its write handle and
        // throws a sharing violation. A running job's log must still be readable.
        var job = StartJob("00458", "ExecutePlan", "00044-Demo");
        job.EnqueueSystemOutput("mid-run");

        var result = JobEventWireStore.Read(_tendrilHome, job);

        Assert.NotNull(result);
        Assert.Equal("mid-run", ExtractText(Assert.Single(result!)));

        job.CloseLogWriters();
    }

    [Fact]
    public void CreatePlanJob_StreamsToASingleFileEvenAfterCompletionRewritesPlanFile()
    {
        // JobCompletionHandler.VerifyCreatePlanResult replaces a CreatePlan job's PlanFile with the folder
        // it produced. The stem must not follow, or the job's artifacts would scatter across two names.
        var job = StartJob("00459", "CreatePlan", "Add a logout button");
        job.EnqueueSystemOutput("during-the-run");

        job.PlanFile = "00075-AddLogoutButton";
        job.EnqueueSystemOutput("at-completion");
        job.CloseLogWriters();

        var eventWires = Directory.GetFiles(Path.Combine(_tendrilHome, "Jobs"), "*.eventwire.jsonl");
        var only = Assert.Single(eventWires);
        Assert.Equal("00459-CreatePlan.eventwire.jsonl", Path.GetFileName(only));
        Assert.Equal(2, File.ReadAllLines(only).Length);
    }

    private static string ExtractText(string serializedEvent)
    {
        var evt = new Ivy.Tendril.Agents.Runtime.JsonEventSerializer().Deserialize(serializedEvent);
        return evt is Ivy.Tendril.Agents.Abstractions.TextEvent t ? t.Text ?? "" : "";
    }
}
