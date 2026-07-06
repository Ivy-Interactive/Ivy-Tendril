using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

public class JobServiceFailureReasonTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    private string CreateValidPlanFolder()
    {
        var dir = Path.Combine(_tempDir.Path, $"plan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var repoDir = Path.Combine(dir, "repo");
        Directory.CreateDirectory(repoDir);
        File.WriteAllText(Path.Combine(dir, "plan.yaml"),
            $"state: Draft\nproject: TestProject\nlevel: NiceToHave\ntitle: Test Plan\ncreated: 2026-01-01T00:00:00Z\nupdated: 2026-01-01T00:00:00Z\nrepos:\n- {repoDir}\nprs: []\ncommits: []\nverifications: []\nrelatedPlans: []\ndependsOn: []\n");
        return dir;
    }
    [Fact]
    public void ExtractFailureReason_EmptyOutput_ReturnsUnknownError()
    {
        var result = JobService.ExtractFailureReason([], "test");
        Assert.Equal("Unknown error (exit code non-zero)", result);
    }

    [Fact]
    public void ExtractFailureReason_EmptyOutputWithExitCode_IncludesExitCodeInMessage()
    {
        var result = JobService.ExtractFailureReason([], "test", 42);
        Assert.Equal("Process exited with code 42", result);
    }

    [Fact]
    public void ExtractFailureReason_StderrLines_ReturnsLastStderrContent()
    {
        var lines = new List<string>
        {
            "Starting process...",
            "[stderr] warning: something minor",
            "Processing...",
            "[stderr] error: connection refused",
            "[stderr] fatal: cannot continue"
        };

        var result = JobService.ExtractFailureReason(lines, "test");
        Assert.Contains("fatal: cannot continue", result);
        Assert.Contains("error: connection refused", result);
    }

    [Fact]
    public void ExtractFailureReason_NoStderr_FallsBackToLastOutputLine()
    {
        var lines = new List<string>
        {
            "Step 1 done",
            "Step 2 done",
            "Build failed with 3 errors"
        };

        var result = JobService.ExtractFailureReason(lines, "test");
        Assert.Equal("Build failed with 3 errors", result);
    }

    [Fact]
    public void ExtractFailureReason_LongLine_PreservesFullLength()
    {
        var longLine = new string('x', 300);
        var lines = new List<string> { longLine };

        var result = JobService.ExtractFailureReason(lines, "test");
        Assert.Equal(300, result.Length);
    }

    [Fact]
    public void ExtractFailureReason_OnlyEmptyLines_ReturnsUnknownError()
    {
        var lines = new List<string> { "", "  ", "" };

        var result = JobService.ExtractFailureReason(lines, "test");
        Assert.Equal("Unknown error (exit code non-zero)", result);
    }

    [Fact]
    public void ExtractFailureReason_EmptyStderrLines_SkipsEmpty()
    {
        var lines = new List<string>
        {
            "[stderr] ",
            "[stderr] actual error message",
            "[stderr]  "
        };

        var result = JobService.ExtractFailureReason(lines, "test");
        Assert.Equal("actual error message", result);
    }

    [Fact]
    public void ExtractFailureReason_MixedOutput_PrefersStderr()
    {
        var lines = new List<string>
        {
            "Some regular output",
            "[stderr] the real error",
            "More regular output after stderr"
        };

        var result = JobService.ExtractFailureReason(lines, "test");
        Assert.Equal("the real error", result);
    }

    [Fact]
    public void CompleteJob_NonZeroExitCode_PopulatesStatusMessage()
    {
        var service = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10));
        var id = service.CreateTestJob(new ExecutePlanArgs(Path.GetTempPath()));
        var job = service.GetJob(id)!;
        job.OutputLines.Enqueue("[stderr] something went wrong");

        service.CompleteJob(id, 1);

        job = service.GetJob(id)!;
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.NotNull(job.StatusMessage);
        Assert.Contains("something went wrong", job.StatusMessage);
    }

    [Fact]
    public void CompleteJob_ZeroExitCode_NullStatusMessage()
    {
        var service = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10));
        var planFolder = CreateValidPlanFolder();
        var id = service.StartJob(new ExecutePlanArgs(planFolder));

        service.CompleteJob(id, 0);

        var job = service.GetJob(id)!;
        Assert.Equal(JobStatus.Completed, job.Status);
        Assert.Null(job.StatusMessage);
    }

    [Fact]
    public void ExtractFailureReason_AnsiCodes_StripsEscapeSequences()
    {
        var lines = new List<string>
        {
            "[stderr] \x1B[31merror: build failed\x1B[0m"
        };

        var result = JobService.ExtractFailureReason(lines, "test");
        Assert.Equal("error: build failed", result);
    }

    [Fact]
    public void ExtractFailureReason_ControlCharacters_NormalizesWhitespace()
    {
        var lines = new List<string>
        {
            "[stderr] error:\tfailed to\t\tcompile\nwith errors"
        };

        var result = JobService.ExtractFailureReason(lines, "test");
        Assert.Equal("error: failed to compile with errors", result);
        Assert.DoesNotContain("\t", result);
        Assert.DoesNotContain("\n", result);
    }

    [Fact]
    public void CompleteJob_WithExistingStatusMessage_PreservesApiSetMessage()
    {
        var service = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10));
        var planFolder = CreateValidPlanFolder();
        var id = service.StartJob(new ExecutePlanArgs(planFolder));
        var job = service.GetJob(id)!;
        job.StatusMessage = "Execution failed (exit code: 1)";
        job.OutputLines.Enqueue("[stderr] some raw stderr output");

        service.CompleteJob(id, 1);

        job = service.GetJob(id)!;
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("Execution failed (exit code: 1)", job.StatusMessage);
    }

    [Fact]
    public void CompleteJob_WithExistingStatusMessageMentioningExitCode_NotOverriddenByAgentAnalyzer()
    {
        // Regression: a pre-set StatusMessage that happens to contain the phrase "exit code"
        // must not be mistaken for the generic fallback and replaced by the agent-level analyzer,
        // even when the analyzer would otherwise recognize the stderr content (e.g. rate limit).
        var service = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10), agentRunner: TestAgentRunner.Create());
        var planFolder = CreateValidPlanFolder();
        var id = service.StartJob(new ExecutePlanArgs(planFolder));
        var job = service.GetJob(id)!;
        job.StatusMessage = "Execution failed (exit code: 1)";
        job.OutputLines.Enqueue("[stderr] rate limit exceeded");

        service.CompleteJob(id, 1);

        job = service.GetJob(id)!;
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("Execution failed (exit code: 1)", job.StatusMessage);
    }

    [Fact]
    public void CompleteJob_ZeroExitCode_WithErrorEvent_MarksAsFailed()
    {
        var service = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10));
        var id = service.CreateTestJob(new ExecutePlanArgs(Path.GetTempPath()));
        var job = service.GetJob(id)!;
        job.OutputLines.Enqueue(
            """{"kind":"error","timestamp":"2026-05-26T12:18:29Z","message":"Access to Meta Llama models is not allowed from unsupported regions","is_retryable":false,"is_auth_error":false}""");

        service.CompleteJob(id, 0);

        job = service.GetJob(id)!;
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Contains("Access to Meta Llama models", job.StatusMessage);
    }

    [Fact]
    public void ReportJobFailure_SetsReportedFailureReason()
    {
        var service = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10));
        var id = service.CreateTestJob(new ExecutePlanArgs(Path.GetTempPath()));

        var ok = service.ReportJobFailure(id, "Worktree creation failed");

        Assert.True(ok);
        Assert.Equal("Worktree creation failed", service.GetJob(id)!.ReportedFailureReason);
    }

    [Fact]
    public void ReportJobFailure_UnknownJob_ReturnsFalse()
    {
        var service = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10));

        Assert.False(service.ReportJobFailure("99999", "nope"));
    }

    [Fact]
    public void CompleteJob_NonZeroExit_WithReportedFailureReason_PrefersReportedReason()
    {
        var service = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10));
        var id = service.CreateTestJob(new ExecutePlanArgs(Path.GetTempPath()));
        var job = service.GetJob(id)!;
        // The promptware first reported progress via `tendril job status`, then declared a
        // failure via `tendril job fail`. The declared reason must win over both the stale
        // progress message and the output-scraping heuristic.
        job.StatusMessage = "Checking dependencies...";
        job.ReportedFailureReason = "Worktree creation failed: .git missing";
        job.OutputLines.Enqueue("[stderr] fatal: some unrelated git noise");

        service.CompleteJob(id, 1);

        job = service.GetJob(id)!;
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("Worktree creation failed: .git missing", job.StatusMessage);
    }

    [Fact]
    public void CompleteJob_NonZeroExit_NoReportedReason_FallsBackToScraper()
    {
        var service = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10));
        var id = service.CreateTestJob(new ExecutePlanArgs(Path.GetTempPath()));
        var job = service.GetJob(id)!;
        job.OutputLines.Enqueue("[stderr] something went wrong");

        service.CompleteJob(id, 1);

        job = service.GetJob(id)!;
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Contains("something went wrong", job.StatusMessage);
    }

    [Fact]
    public void CompleteJob_ZeroExitCode_WithoutErrorEvent_RemainsCompleted()
    {
        var service = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10));
        var planFolder = CreateValidPlanFolder();
        var id = service.StartJob(new ExecutePlanArgs(planFolder));
        var job = service.GetJob(id)!;
        job.OutputLines.Enqueue(
            """{"kind":"text","timestamp":"2026-05-26T12:18:29Z","text":"All done"}""");

        service.CompleteJob(id, 0);

        job = service.GetJob(id)!;
        Assert.Equal(JobStatus.Completed, job.Status);
        Assert.Null(job.StatusMessage);
    }

    [Fact]
    public void ExtractFailureReason_NoMatchingContentWithExitCode_IncludesExitCodeInMessage()
    {
        var lines = new List<string> { "", "  ", "" };

        var result = JobService.ExtractFailureReason(lines, "test", 42);

        Assert.Equal("Process exited with code 42", result);
    }

    [Fact]
    public void CompleteJob_GenericFallback_ConsultsAgentLevelAnalyzer()
    {
        var service = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10), agentRunner: TestAgentRunner.Create());
        var id = service.CreateTestJob(new ExecutePlanArgs(Path.GetTempPath()));

        service.CompleteJob(id, 1);

        var job = service.GetJob(id)!;
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("Claude Code exited with code 1", job.StatusMessage);
    }

    [Fact]
    public void CompleteJob_StderrMatchesAnalyzer_PrefersAnalyzerOverRawStderrText()
    {
        // Even though the text-based scan can already extract a specific stderr line here (so it
        // wouldn't hit the generic fallback), the provider-specific analyzer understands what that
        // line actually means (a retryable rate limit) and should still be consulted first.
        var service = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10), agentRunner: TestAgentRunner.Create());
        var id = service.CreateTestJob(new ExecutePlanArgs(Path.GetTempPath()));
        var job = service.GetJob(id)!;
        job.OutputLines.Enqueue("[stderr] rate limit exceeded");

        service.CompleteJob(id, 1);

        job = service.GetJob(id)!;
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("Rate limited by the API (Wait before retrying or switch to a different model)", job.StatusMessage);
    }

    [Fact]
    public void ExtractFailureReason_FailedResultEvent_SessionLimit_ReturnsCleanMessage()
    {
        var lines = new List<string>
        {
            """{"kind":"result","response":"You have hit your session limit, resets 4pm (Europe/Stockholm)","is_success":false,"duration_ms":557}"""
        };

        var result = JobService.ExtractFailureReason(lines, "test");

        Assert.StartsWith("Claude usage limit reached:", result);
        Assert.Contains("session limit", result);
        Assert.Contains("resets 4pm", result);
        Assert.DoesNotContain("\"kind\"", result);
        Assert.DoesNotContain("is_success", result);
    }

    [Fact]
    public void ExtractFailureReason_FailedResultEvent_Generic_ReturnsSanitizedFirstLine()
    {
        var lines = new List<string>
        {
            """{"kind":"result","response":"Something went wrong during the run.\nMore details here.","is_success":false}"""
        };

        var result = JobService.ExtractFailureReason(lines, "test");

        Assert.Equal("Something went wrong during the run.", result);
    }

    [Fact]
    public void CompleteJob_NonZeroExit_SessionLimitResult_MarksFailedWithCleanMessage()
    {
        // Regression for CreatePlan job 00292, which failed with the raw serialized
        // ResultEvent JSON blob shown as the job's failure reason instead of Claude's message.
        var service = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10));
        var id = service.CreateTestJob(new ExecutePlanArgs(Path.GetTempPath()));
        var job = service.GetJob(id)!;
        job.OutputLines.Enqueue(
            """{"kind":"result","response":"You have hit your session limit, resets 4pm (Europe/Stockholm)","is_success":false,"duration_ms":557}""");

        service.CompleteJob(id, 1);

        job = service.GetJob(id)!;
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Contains("session limit", job.StatusMessage);
        Assert.Contains("resets 4pm", job.StatusMessage);
    }

    [Fact]
    public void CompleteJob_ZeroExitCode_FailedSessionLimitResult_MarksFailedWithCleanMessage()
    {
        var service = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10));
        var id = service.CreateTestJob(new ExecutePlanArgs(Path.GetTempPath()));
        var job = service.GetJob(id)!;
        job.OutputLines.Enqueue(
            """{"kind":"result","response":"You have hit your session limit, resets 4pm (Europe/Stockholm)","is_success":false,"duration_ms":557}""");

        service.CompleteJob(id, 0);

        job = service.GetJob(id)!;
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Contains("session limit", job.StatusMessage);
    }

    [Fact]
    public void CompleteJob_ZeroExitCode_SuccessfulResultEvent_RemainsCompleted()
    {
        var service = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10));
        var planFolder = CreateValidPlanFolder();
        var id = service.StartJob(new ExecutePlanArgs(planFolder));
        var job = service.GetJob(id)!;
        job.OutputLines.Enqueue(
            """{"kind":"result","response":"All done","is_success":true,"duration_ms":557}""");

        service.CompleteJob(id, 0);

        job = service.GetJob(id)!;
        Assert.Equal(JobStatus.Completed, job.Status);
        Assert.Null(job.StatusMessage);
    }
}
