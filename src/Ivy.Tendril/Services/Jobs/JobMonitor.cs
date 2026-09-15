using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Ivy.Helpers;
using Ivy.Tendril.Models;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services.Jobs;

internal class JobMonitor
{
    private readonly string _id;
    private readonly JobLaunchContext _ctx;
    private readonly Process _process;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _timeoutCts;
    private readonly DateTime _startedAt;

    internal JobMonitor(string id, JobLaunchContext ctx, Process process, ILogger logger)
    {
        _id = id;
        _ctx = ctx;
        _process = process;
        _logger = logger;
        _timeoutCts = new CancellationTokenSource();
        _startedAt = DateTime.UtcNow;
    }

    internal void Start()
    {
        var job = _ctx.Jobs.GetValueOrDefault(_id);
        if (job != null)
            job.TimeoutCts = _timeoutCts;

        Task.Run(MonitorProcessAsync);
        _ = RunJobTimeoutWatchdog();
        _ = RunStaleOutputWatchdog(_id, _timeoutCts, _ctx.Jobs, _ctx.StaleOutputTimeout);
        _ = RunPostResultGraceWatchdog(_id, _timeoutCts, _ctx.Jobs, _process, _logger);
        _ = RunProviderFailureWatchdog(_id, _timeoutCts, _ctx.Jobs, _process, _logger);

        // Status updates now arrive via HTTP (PUT /api/jobs/{id}/status)
        // — no file polling needed.
    }

    private async Task MonitorProcessAsync()
    {
        _logger.LogDebug("Job {JobId}: Monitor task started", _id);

        try
        {
            var waitTask = _process.WaitForExitOrKillAsync(_timeoutCts.Token);
            var completedTask = await Task.WhenAny(waitTask, PollHardTimeoutAsync());

            if (completedTask == waitTask)
                HandleNormalExit(await waitTask);
            else
                HandleHardTimeout();
        }
        catch (ObjectDisposedException)
        {
            _logger.LogDebug("Job {JobId}: Monitor task exiting (CTS disposed, job completed elsewhere)", _id);

            // Normally the CTS is only disposed once the job has already been completed elsewhere.
            // If it somehow got disposed while the job is still Running, don't return silently — that
            // would leave the job stranded with no monitor left to ever complete it.
            if (_ctx.Jobs.TryGetValue(_id, out var job) && job.Status == JobStatus.Running)
            {
                _logger.LogWarning("Job {JobId}: CTS disposed but job still Running — completing as timeout", _id);

                try
                {
                    if (!_process.HasExited)
                        _process.Kill(entireProcessTree: true);
                }
                catch (Exception killEx)
                {
                    _logger.LogWarning(killEx, "Job {JobId}: Failed to kill process during CTS-disposed recovery", _id);
                }

                _ctx.CompleteJob(_id, null, true, false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId}: Monitor task exception", _id);
            CrashLog.Write($"[{DateTime.UtcNow:O}] JobService process monitor exception for job {_id}: {ex}");
            _ctx.CompleteJob(_id, null, false, false);
        }
    }

    private void HandleNormalExit(bool normalExit)
    {
        if (_ctx.Jobs.TryGetValue(_id, out var postResultJob) && postResultJob.PostResultGraceExceeded)
        {
            _logger.LogInformation("Job {JobId}: Process terminated after post-result grace period. Completing with ResultEvent outcome", _id);
            var exitCode = postResultJob.LastResultEvent?.IsSuccess == true ? 0 : (postResultJob.LastResultEvent?.ExitCode ?? 1);
            postResultJob.ExitCode = exitCode;
            _ctx.CompleteJob(_id, exitCode, false, false);
            return;
        }

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
        if (_ctx.Jobs.TryGetValue(_id, out var postResultJob) && postResultJob.PostResultGraceExceeded)
        {
            _logger.LogInformation("Job {JobId}: Hard timeout reached after post-result grace termination. Completing with ResultEvent outcome", _id);
            var exitCode = postResultJob.LastResultEvent?.IsSuccess == true ? 0 : (postResultJob.LastResultEvent?.ExitCode ?? 1);
            postResultJob.ExitCode = exitCode;
            _ctx.CompleteJob(_id, exitCode, false, false);
            return;
        }

        var hardTimeout = _ctx.JobTimeout() + TimeSpan.FromMinutes(5);
        _logger.LogError("Job {JobId}: HARD TIMEOUT after {Minutes} minutes - process may still be running",
            _id, hardTimeout.TotalMinutes);
        CrashLog.Write($"[{DateTime.UtcNow:O}] Job {_id} hit hard timeout after {hardTimeout.TotalMinutes} minutes - WaitForExitOrKillAsync did not complete");

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
        Func<TimeSpan> staleOutputTimeout)
    {
        // Baseline for staleness before any output arrives. Captured when monitoring begins — i.e.
        // after the pre-launch "before" hooks and process start — so slow hook setup does not count
        // against the no-output window. A job that never emits output is still declared stale
        // relative to this baseline instead of evading the stale-output timeout forever (#1455).
        var monitoringStartedAt = DateTime.UtcNow;
        try
        {
            while (!timeoutCts.Token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), timeoutCts.Token);
                if (!jobs.TryGetValue(id, out var job) || job.Status != JobStatus.Running)
                    return;

                if (job.Process is { HasExited: true })
                    return;

                // Once a terminal ResultEvent has been received, the agent has finished its work;
                // lack of further output is expected while the process wraps up.
                // The post-result grace watchdog handles terminating the process if it lingers.
                if (job.LastResultEvent != null)
                    return;

                var currentTimeout = staleOutputTimeout();
                if (currentTimeout <= TimeSpan.Zero)
                    continue;

                var anchor = job.LastOutputAt ?? monitoringStartedAt;
                if (DateTime.UtcNow - anchor < currentTimeout)
                    continue;

                job.StaleOutputDetected = true;
                try { timeoutCts.Cancel(); } catch (ObjectDisposedException) { }
                return;
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    internal static async Task RunPostResultGraceWatchdog(
        string id,
        CancellationTokenSource timeoutCts,
        ConcurrentDictionary<string, JobItem> jobs,
        Process process,
        ILogger logger,
        TimeSpan? gracePeriodOverride = null,
        TimeSpan? tickInterval = null,
        TimeSpan? failedResultGraceOverride = null)
    {
        var gracePeriod = gracePeriodOverride ?? TimeSpan.FromSeconds(20);
        // A terminal ResultEvent that reports failure means the agent is not going to produce
        // anything else, so there is nothing worth waiting twenty seconds for. The full grace stays
        // for a successful result, where the process may legitimately still be flushing.
        var failedResultGrace = failedResultGraceOverride ?? TimeSpan.FromSeconds(2);
        var interval = tickInterval ?? TimeSpan.FromSeconds(1);
        try
        {
            while (!timeoutCts.Token.IsCancellationRequested)
            {
                await Task.Delay(interval, timeoutCts.Token);
                if (!jobs.TryGetValue(id, out var job) || job.Status != JobStatus.Running)
                    return;

                if (job.Process is { HasExited: true } || process.HasExited)
                    return;

                if (job.ResultReceivedAt is { } resultAt)
                {
                    var effectiveGrace = job.LastResultEvent is { IsSuccess: false } ? failedResultGrace : gracePeriod;
                    if (DateTime.UtcNow - resultAt >= effectiveGrace)
                    {
                        job.PostResultGraceExceeded = true;
                        logger.LogInformation(
                            "Job {JobId}: Agent emitted terminal ResultEvent but process did not exit within {GraceSeconds}s grace period — terminating process tree",
                            id, (int)effectiveGrace.TotalSeconds);

                        try
                        {
                            if (!process.HasExited)
                                process.Kill(entireProcessTree: true);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Job {JobId}: Failed to kill process after post-result grace period", id);
                        }
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    /// <summary>
    ///     Kills an agent that has hit a provider wall — an exhausted quota, a failed auth — instead of
    ///     letting it sit out the CLI's own <c>--print-timeout</c> (7200s in the incident this was
    ///     written for) while every attempt 429s. The kill is the whole mechanism:
    ///     <c>MonitorProcessAsync</c> observes the exit and completes the job through the normal path,
    ///     which is how <see cref="RunPostResultGraceWatchdog" /> already works.
    ///     <para>
    ///         Deliberately does <em>not</em> cancel the timeout CTS: that is the timeout signal, and
    ///         using it here would relabel a quota wall as a timeout — exactly the wrong post-mortem.
    ///     </para>
    /// </summary>
    internal static async Task RunProviderFailureWatchdog(
        string id,
        CancellationTokenSource timeoutCts,
        ConcurrentDictionary<string, JobItem> jobs,
        Process process,
        ILogger logger,
        TimeSpan? tickInterval = null)
    {
        var interval = tickInterval ?? TimeSpan.FromSeconds(1);
        try
        {
            while (!timeoutCts.Token.IsCancellationRequested)
            {
                await Task.Delay(interval, timeoutCts.Token);
                if (!jobs.TryGetValue(id, out var job) || job.Status != JobStatus.Running)
                    return;

                if (job.Process is { HasExited: true } || process.HasExited)
                    return;

                if (!job.ProviderFailureDetected)
                    continue;

                logger.LogWarning(
                    "Job {JobId}: Provider failure detected ({Reason}) — terminating the agent rather than waiting out its print timeout",
                    id, job.ProviderFailureMessage ?? "unknown provider error");

                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Job {JobId}: Failed to kill process after provider failure", id);
                }
                return;
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }


    private Task RunJobTimeoutWatchdog()
        => RunJobTimeoutWatchdog(_id, _timeoutCts, _ctx.Jobs, _ctx.JobTimeout, _startedAt);

    internal static async Task RunJobTimeoutWatchdog(
        string id,
        CancellationTokenSource timeoutCts,
        ConcurrentDictionary<string, JobItem> jobs,
        Func<TimeSpan> jobTimeout,
        DateTime startedAt,
        TimeSpan? tickInterval = null)
    {
        var interval = tickInterval ?? TimeSpan.FromSeconds(5);
        try
        {
            while (!timeoutCts.Token.IsCancellationRequested)
            {
                await Task.Delay(interval, timeoutCts.Token);
                if (!jobs.TryGetValue(id, out var job) || job.Status != JobStatus.Running)
                    return;

                if (DateTime.UtcNow - startedAt >= jobTimeout())
                {
                    try { timeoutCts.Cancel(); } catch (ObjectDisposedException) { }
                    return;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task PollHardTimeoutAsync()
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                var hardTimeout = _ctx.JobTimeout() + TimeSpan.FromMinutes(5);
                if (DateTime.UtcNow - _startedAt >= hardTimeout)
                    return;
            }
        }
        catch (OperationCanceledException) { }
    }

    private static bool IsValidPlanId(string? planId)
    {
        return !string.IsNullOrEmpty(planId)
            && Regex.IsMatch(planId, @"^\d{5}$")
            && planId != "01234";
    }
}
