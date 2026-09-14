using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

public class JobLogWriterFinalOutputTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    private JobService CreateJobService()
    {
        var configService = new ConfigService(new TendrilSettings(), _tempDir.Path);
        return new JobService(configService);
    }

    private string ReadLog(string jobId, string planId)
        => File.ReadAllText(Path.Combine(_tempDir.Path, "Jobs", $"{jobId}-{planId}-ExecutePlan.md"));

    [Fact]
    public void WriteJobLog_TruncatedRun_WritesPartialTextUnderFlaggedHeading()
    {
        var jobService = CreateJobService();
        var job = new JobItem { Id = "10", Type = "ExecutePlan", PlanFile = "00010-TestPlan", Status = JobStatus.Failed };
        job.OutputLines.Enqueue("""{"kind":"text","text":"I'll start by reading the plan and understanding what needs to be done."}""");
        job.OutputLines.Enqueue(
            """{"kind":"error","message":"Model output truncated at the max output token limit (4096 output tokens in the final step); any pending tool call was aborted.","code":"output_truncated"}""");

        jobService.WriteJobLog(job);

        var log = ReadLog("10", "00010");
        Assert.Contains("## Final Output (truncated)", log);
        Assert.Contains("*Model output truncated at the max output token limit (4096 output tokens in the final step); any pending tool call was aborted.*", log);
        Assert.Contains("I'll start by reading the plan and understanding what needs to be done.", log);
    }

    [Fact]
    public void WriteJobLog_TruncatedRun_IgnoresTendrilSyntheticText()
    {
        var jobService = CreateJobService();
        var job = new JobItem { Id = "11", Type = "ExecutePlan", PlanFile = "00011-TestPlan", Status = JobStatus.Failed };
        job.OutputLines.Enqueue("""{"kind":"text","text":"the model's own partial answer"}""");
        job.OutputLines.Enqueue("""{"kind":"text","text":"[Tendril] Permission denied: rm -rf /"}""");
        job.OutputLines.Enqueue("""{"kind":"error","message":"stopped early","code":"output_truncated"}""");

        jobService.WriteJobLog(job);

        var log = ReadLog("11", "00011");
        Assert.Contains("the model's own partial answer", log);
        Assert.DoesNotContain("Permission denied", log);
    }

    [Fact]
    public void WriteJobLog_TruncatedRun_ConcatenatesDeltaText()
    {
        var jobService = CreateJobService();
        var job = new JobItem { Id = "12", Type = "ExecutePlan", PlanFile = "00012-TestPlan", Status = JobStatus.Failed };
        job.OutputLines.Enqueue("""{"kind":"text","text":"first half ","delta":true}""");
        job.OutputLines.Enqueue("""{"kind":"text","text":"second half","delta":true}""");
        job.OutputLines.Enqueue("""{"kind":"error","message":"stopped early","code":"output_truncated"}""");

        jobService.WriteJobLog(job);

        var log = ReadLog("12", "00012");
        Assert.Contains("first half second half", log);
    }

    [Fact]
    public void WriteJobLog_UnhandledStopReason_WritesIncompleteHeading()
    {
        var jobService = CreateJobService();
        var job = new JobItem { Id = "13", Type = "ExecutePlan", PlanFile = "00013-TestPlan", Status = JobStatus.Failed };
        job.OutputLines.Enqueue("""{"kind":"text","text":"partial work before an unhandled stop"}""");
        job.OutputLines.Enqueue("""{"kind":"error","message":"Unhandled stop reason: tool_use_limit","code":"unhandled_stop_reason"}""");

        jobService.WriteJobLog(job);

        var log = ReadLog("13", "00013");
        Assert.Contains("## Final Output (incomplete)", log);
        Assert.Contains("partial work before an unhandled stop", log);
    }

    [Fact]
    public void WriteJobLog_SuccessfulRun_KeepsPlainHeading()
    {
        var jobService = CreateJobService();
        var job = new JobItem { Id = "14", Type = "ExecutePlan", PlanFile = "00014-TestPlan", Status = JobStatus.Completed };
        job.OutputLines.Enqueue("""{"kind":"result","response":"All done.","is_success":true}""");

        jobService.WriteJobLog(job);

        var log = ReadLog("14", "00014");
        Assert.Contains("## Final Output\n", log);
        Assert.DoesNotContain("## Final Output (", log);
        Assert.Contains("All done.", log);
    }

    [Fact]
    public void WriteJobLog_TextOnlyWithoutTruncation_OmitsFinalOutput()
    {
        var jobService = CreateJobService();
        var job = new JobItem { Id = "15", Type = "ExecutePlan", PlanFile = "00015-TestPlan", Status = JobStatus.Completed };
        job.OutputLines.Enqueue("""{"kind":"text","text":"some intermediate narration"}""");

        jobService.WriteJobLog(job);

        var log = ReadLog("15", "00015");
        Assert.DoesNotContain("## Final Output", log);
        Assert.DoesNotContain("some intermediate narration", log);
    }
}
