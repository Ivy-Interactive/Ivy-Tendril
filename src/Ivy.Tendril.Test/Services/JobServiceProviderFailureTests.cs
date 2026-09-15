using System.Collections.Concurrent;
using System.Diagnostics;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Antigravity;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Jobs;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test.Services;

/// <summary>
///     The job-side half of surfacing a provider wall: a quota error has to reach the eventwire, has to
///     fail the job fast instead of sitting out a 7200s <c>--print-timeout</c>, and has to say what
///     actually happened rather than "No output for 60 minutes".
/// </summary>
public class JobServiceProviderFailureTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    private static JobService CreateService(TimeSpan? jobTimeout = null, TimeSpan? staleTimeout = null)
    {
        SynchronizationContext.SetSynchronizationContext(null);
        return new JobService(
            jobTimeout ?? TimeSpan.FromMinutes(30),
            staleTimeout ?? TimeSpan.FromMinutes(10));
    }

    // The lines of src/Ivy.Tendril.Agents.Test/Antigravity/Fixtures/provider-quota-error.jsonl, inlined
    // so this project does not have to reach into another test project's content files.
    private const string InitLine =
        "{\"event\":\"init\",\"conversation_id\":\"da84f37e-fe47-4751-9a3a-9a8eaa49dbbf\",\"init\":{\"model\":\"gemini-3.8-flash\"}}";

    private static string ErrorStepLine(int stepIndex) =>
        $"{{\"event\":\"step_update\",\"step_update\":{{\"conversation_id\":\"da84f37e-fe47-4751-9a3a-9a8eaa49dbbf\",\"step_index\":{stepIndex},\"state\":\"DONE\",\"step_type\":\"error_message\",\"duration_seconds\":0}}}}";

    private static string QuotaErrorStepLine(int stepIndex) =>
        $"{{\"event\":\"step_update\",\"step_update\":{{\"step_index\":{stepIndex},\"state\":\"DONE\",\"step_type\":\"error_message\",\"duration_seconds\":0," +
        "\"text\":\"API error (attempt 5): RESOURCE_EXHAUSTED (code 429): Resource has been exhausted (e.g. check quota).\"}}";

    private const string FailedResultLine =
        "{\"event\":\"result\",\"result\":{\"conversation_id\":\"da84f37e-fe47-4751-9a3a-9a8eaa49dbbf\",\"status\":\"ERROR\"," +
        "\"error\":\"API error (attempt 5): RESOURCE_EXHAUSTED (code 429): Resource has been exhausted (e.g. check quota).\",\"duration_seconds\":9.4}}";

    private static JobItem AntigravityJob(string id = "job-provider-failure") => new()
    {
        Id = id,
        Status = JobStatus.Running,
        Provider = "antigravity",
        EventParser = new AntigravityEventParser(),
    };

    // The spinner in JobsApp.Helpers renders "Starting..." for a Running job with nothing on the wire.
    // Eight provider errors used to produce zero eventwire lines, which is why fourteen jobs looked
    // like they had never started.
    [Fact]
    public void EnqueueOutput_ErrorMessageStep_AdvancesLastOutputAt()
    {
        var job = AntigravityJob();

        job.EnqueueOutput(InitLine);
        for (var i = 1; i <= 8; i++)
            job.EnqueueOutput(ErrorStepLine(i));

        Assert.NotEmpty(job.OutputLines);
        // The launcher's OutputDataReceived handler stamps LastOutputAt for every line it passes here,
        // so a non-empty wire is exactly what makes the "no output yet" spinner start counting.
        Assert.Equal(8, job.OutputLines.Count(l => l.Contains("agent reported an error", StringComparison.Ordinal)));
    }

    [Fact]
    public void EnqueueOutput_ThreeConsecutiveQuotaErrors_SetsProviderFailureDetected()
    {
        var job = AntigravityJob();

        job.EnqueueOutput(QuotaErrorStepLine(1));
        Assert.False(job.ProviderFailureDetected);
        job.EnqueueOutput(QuotaErrorStepLine(2));
        Assert.False(job.ProviderFailureDetected);
        job.EnqueueOutput(QuotaErrorStepLine(3));

        Assert.True(job.ProviderFailureDetected);
        Assert.Equal(3, job.ProviderErrorCount);
        Assert.Contains("antigravity", job.ProviderFailureMessage);
        Assert.Contains("RESOURCE_EXHAUSTED", job.ProviderFailureMessage);
    }

    // A quota wall clears by itself, so real progress between errors means there is no wall — the
    // streak has to start over rather than accumulate across an entire long run.
    [Fact]
    public void EnqueueOutput_QuotaErrorsInterruptedByProgress_DoesNotSetProviderFailureDetected()
    {
        var job = AntigravityJob();

        job.EnqueueOutput(QuotaErrorStepLine(1));
        job.EnqueueOutput(QuotaErrorStepLine(2));
        job.EnqueueOutput("{\"event\":\"step_update\",\"step_update\":{\"step_type\":\"agent_response\",\"text_delta\":\"working on it\"}}");
        job.EnqueueOutput(QuotaErrorStepLine(3));

        Assert.False(job.ProviderFailureDetected);
        Assert.Equal(1, job.ProviderErrorCount);
    }

    // The bug's real signature: the terminal result carries the 429 even when the individual error
    // steps carry no text at all.
    [Fact]
    public void EnqueueOutput_FailedResultWithQuotaError_SetsProviderFailureDetected()
    {
        var job = AntigravityJob();

        job.EnqueueOutput(InitLine);
        job.EnqueueOutput(FailedResultLine);

        Assert.True(job.ProviderFailureDetected);
        Assert.Contains("antigravity/gemini-3.8-flash", job.ProviderFailureMessage);
        Assert.Contains("RESOURCE_EXHAUSTED", job.ProviderFailureMessage);
    }

    [Fact]
    public async Task ProviderFailureWatchdog_TripsWellBeforePrintTimeout()
    {
        using var cts = new CancellationTokenSource();
        var jobs = new ConcurrentDictionary<string, JobItem>();
        var job = AntigravityJob("job-provider-watchdog");
        job.EnqueueOutput(InitLine);
        job.EnqueueOutput(FailedResultLine);
        Assert.True(job.ProviderFailureDetected);
        jobs[job.Id] = job;

        using var sleepProc = Process.Start(new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "sleep",
            Arguments = OperatingSystem.IsWindows() ? "/c ping 127.0.0.1 -n 30" : "30",
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;
        job.Process = sleepProc;

        try
        {
            var sw = Stopwatch.StartNew();
            await JobMonitor.RunProviderFailureWatchdog(
                job.Id, cts, jobs, sleepProc, NullLogger.Instance,
                tickInterval: TimeSpan.FromMilliseconds(5));
            sw.Stop();

            Assert.True(sleepProc.WaitForExit(3000), "Provider failure must kill the agent process");
            // The CLI's own --print-timeout was 7200s in the incident; anything in this ballpark proves
            // the job no longer waits it out.
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"Watchdog took {sw.Elapsed}");

            // Deliberately not cancelled: the timeout CTS is the timeout signal, and using it here would
            // relabel a quota wall as a timeout.
            Assert.False(cts.IsCancellationRequested);
        }
        finally
        {
            try { if (!sleepProc.HasExited) sleepProc.Kill(true); } catch { }
        }
    }

    [Fact]
    public void SetCompletionStatus_ProviderFailure_MessageDiffersFromStaleOutput()
    {
        var service = CreateService();

        var providerId = service.CreateTestJob(new ExecutePlanArgs(_tempDir.Path));
        var providerJob = service.GetJob(providerId)!;
        providerJob.Provider = "antigravity";
        providerJob.EventParser = new AntigravityEventParser();
        providerJob.EnqueueOutput(InitLine);
        providerJob.EnqueueOutput(FailedResultLine);

        var staleId = service.CreateTestJob(new ExecutePlanArgs(_tempDir.Path));
        var staleJob = service.GetJob(staleId)!;
        staleJob.StartedAt = DateTime.UtcNow.AddMinutes(-20);
        staleJob.LastOutputAt = null;

        service.CompleteJob(providerId, null, true);
        service.RunStuckJobCheck();

        providerJob = service.GetJob(providerId)!;
        staleJob = service.GetJob(staleId)!;

        // Failed, not Timeout: an exhausted quota is a failure, and it was known within seconds.
        Assert.Equal(JobStatus.Failed, providerJob.Status);
        Assert.Contains("antigravity", providerJob.StatusMessage);
        Assert.Contains("gemini-3.8-flash", providerJob.StatusMessage);
        Assert.Contains("RESOURCE_EXHAUSTED", providerJob.StatusMessage);

        Assert.Equal(JobStatus.Timeout, staleJob.Status);
        Assert.Contains("No output for 10 minutes", staleJob.StatusMessage);

        Assert.NotEqual(providerJob.StatusMessage, staleJob.StatusMessage);
    }

    // A failed terminal result means the agent has nothing left to say, so there is nothing worth
    // waiting the full twenty seconds for.
    [Fact]
    public async Task PostResultGrace_FailedResult_UsesShortGrace()
    {
        using var cts = new CancellationTokenSource();
        var jobs = new ConcurrentDictionary<string, JobItem>();
        var job = new JobItem
        {
            Id = "job-failed-result-grace",
            Status = JobStatus.Running,
            ResultReceivedAt = DateTime.UtcNow.AddSeconds(-5),
            LastResultEvent = new ResultEvent { Kind = AgentEventKind.Result, IsSuccess = false, Error = "RESOURCE_EXHAUSTED" },
        };
        jobs[job.Id] = job;

        using var sleepProc = Process.Start(new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "sleep",
            Arguments = OperatingSystem.IsWindows() ? "/c ping 127.0.0.1 -n 30" : "30",
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;
        job.Process = sleepProc;

        try
        {
            // Real grace values, an accelerated tick: five seconds since the result is past the 2s
            // failed-result grace but well inside the 20s success grace, so only the short one can trip.
            await JobMonitor.RunPostResultGraceWatchdog(
                job.Id, cts, jobs, sleepProc, NullLogger.Instance,
                tickInterval: TimeSpan.FromMilliseconds(5));

            Assert.True(job.PostResultGraceExceeded);
            Assert.True(sleepProc.WaitForExit(3000));
        }
        finally
        {
            try { if (!sleepProc.HasExited) sleepProc.Kill(true); } catch { }
        }
    }

    [Fact]
    public async Task PostResultGrace_SuccessfulResult_StillUsesLongGrace()
    {
        using var cts = new CancellationTokenSource();
        var jobs = new ConcurrentDictionary<string, JobItem>();
        var job = new JobItem
        {
            Id = "job-success-result-grace",
            Status = JobStatus.Running,
            ResultReceivedAt = DateTime.UtcNow.AddSeconds(-5),
            LastResultEvent = new ResultEvent { Kind = AgentEventKind.Result, IsSuccess = true },
        };
        jobs[job.Id] = job;

        using var sleepProc = Process.Start(new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "sleep",
            Arguments = OperatingSystem.IsWindows() ? "/c ping 127.0.0.1 -n 30" : "30",
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;
        job.Process = sleepProc;

        try
        {
            var watchdog = JobMonitor.RunPostResultGraceWatchdog(
                job.Id, cts, jobs, sleepProc, NullLogger.Instance,
                tickInterval: TimeSpan.FromMilliseconds(5));

            await Task.Delay(TimeSpan.FromMilliseconds(300));

            Assert.False(job.PostResultGraceExceeded);
            Assert.False(sleepProc.HasExited);

            cts.Cancel();
            await Task.WhenAny(watchdog, Task.Delay(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            try { if (!sleepProc.HasExited) sleepProc.Kill(true); } catch { }
        }
    }
}
