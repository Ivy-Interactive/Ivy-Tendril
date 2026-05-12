using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Ivy.Helpers;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services;

internal class JobMonitor
{
    private readonly string _id;
    private readonly JobLaunchContext _ctx;
    private readonly Process _process;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _timeoutCts;
    private readonly TimeSpan _hardTimeout;

    internal JobMonitor(string id, JobLaunchContext ctx, Process process, ILogger logger)
    {
        _id = id;
        _ctx = ctx;
        _process = process;
        _logger = logger;
        _timeoutCts = new CancellationTokenSource(ctx.JobTimeout);
        _hardTimeout = ctx.JobTimeout + TimeSpan.FromMinutes(5);
    }

    internal void Start()
    {
        var job = _ctx.Jobs.GetValueOrDefault(_id);
        if (job != null)
            job.TimeoutCts = _timeoutCts;

        Task.Run(MonitorProcessAsync);

        if (_ctx.StaleOutputTimeout > TimeSpan.Zero)
            _ = RunStaleOutputWatchdog(_id, _timeoutCts, _ctx.Jobs, _ctx.StaleOutputTimeout);

        if (job != null && !string.IsNullOrEmpty(job.StatusFilePath))
            _ = RunStatusFilePoller(_id, _timeoutCts, _ctx.Jobs);
    }

    private async Task MonitorProcessAsync()
    {
        var hardTimeoutCts = new CancellationTokenSource(_hardTimeout);
        _logger.LogDebug("Job {JobId}: Monitor task started", _id);

        try
        {
            var waitTask = _process.WaitForExitOrKillAsync(_timeoutCts.Token);
            var completedTask = await Task.WhenAny(waitTask, Task.Delay(_hardTimeout, hardTimeoutCts.Token));

            if (completedTask == waitTask)
                HandleNormalExit(await waitTask);
            else
                HandleHardTimeout();
        }
        catch (ObjectDisposedException)
        {
            _logger.LogDebug("Job {JobId}: Monitor task exiting (CTS disposed, job completed elsewhere)", _id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId}: Monitor task exception", _id);
            CrashLog.Write($"[{DateTime.UtcNow:O}] JobService process monitor exception for job {_id}: {ex}");
            _ctx.CompleteJob(_id, null, false, false);
        }
        finally
        {
            hardTimeoutCts.Dispose();
        }
    }

    private void HandleNormalExit(bool normalExit)
    {
        if (!normalExit)
        {
            _logger.LogWarning("Job {JobId}: Process killed after timeout", _id);
            _ctx.CompleteJob(_id, null, true, false);
            return;
        }

        if (_ctx.Jobs.TryGetValue(_id, out var j) && j.StaleOutputDetected)
        {
            _logger.LogInformation("Job {JobId}: Process exited, stale output detected", _id);
            _ctx.CompleteJob(_id, null, true, true);
            return;
        }

        _logger.LogInformation("Job {JobId}: Process exited with code {ExitCode}", _id, _process.ExitCode);
        if (_ctx.Jobs.TryGetValue(_id, out var exitJob))
            exitJob.ExitCode = _process.ExitCode;
        _ctx.CompleteJob(_id, _process.ExitCode, false, false);
    }

    private void HandleHardTimeout()
    {
        _logger.LogError("Job {JobId}: HARD TIMEOUT after {Minutes} minutes - process may still be running",
            _id, _hardTimeout.TotalMinutes);
        CrashLog.Write($"[{DateTime.UtcNow:O}] Job {_id} hit hard timeout after {_hardTimeout.TotalMinutes} minutes - WaitForExitOrKillAsync did not complete");

        try
        {
            if (!_process.HasExited)
            {
                _logger.LogWarning("Job {JobId}: Attempting emergency kill", _id);
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception killEx)
        {
            _logger.LogError(killEx, "Job {JobId}: Emergency kill failed", _id);
        }

        _ctx.CompleteJob(_id, null, true, false);
    }

    internal static async Task RunStaleOutputWatchdog(
        string id,
        CancellationTokenSource timeoutCts,
        ConcurrentDictionary<string, JobItem> jobs,
        TimeSpan staleOutputTimeout)
    {
        try
        {
            while (!timeoutCts.Token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), timeoutCts.Token);
                if (!jobs.TryGetValue(id, out var job) || job.Status != JobStatus.Running)
                    return;
                if (!job.LastOutputAt.HasValue)
                    continue;
                if (DateTime.UtcNow - job.LastOutputAt.Value < staleOutputTimeout)
                    continue;

                job.StaleOutputDetected = true;
                try { timeoutCts.Cancel(); } catch (ObjectDisposedException) { }
                return;
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    internal static async Task RunStatusFilePoller(
        string id,
        CancellationTokenSource timeoutCts,
        ConcurrentDictionary<string, JobItem> jobs)
    {
        try
        {
            while (!timeoutCts.Token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), timeoutCts.Token);
                if (!jobs.TryGetValue(id, out var job) || job.Status != JobStatus.Running)
                    return;
                if (string.IsNullOrEmpty(job.StatusFilePath))
                    return;

                var payload = JobStatusFile.Read(job.StatusFilePath);
                if (payload == null)
                    continue;

                job.StatusMessage = payload.Message;
                if (IsValidPlanId(payload.PlanId))
                    job.ReportedPlanId = payload.PlanId;
                if (!string.IsNullOrEmpty(payload.PlanTitle))
                    job.ReportedPlanTitle = payload.PlanTitle;
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private static bool IsValidPlanId(string? planId)
    {
        return !string.IsNullOrEmpty(planId)
            && Regex.IsMatch(planId, @"^\d{5}$")
            && planId != "01234";
    }
}
