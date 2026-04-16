using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Ivy.Helpers;
using Ivy.Tendril.Apps;
using Ivy.Tendril.Apps.Jobs;
using Ivy.Tendril.Apps.Plans;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Ivy.Tendril.Services;

public record JobNotification(string Title, string Message, bool IsSuccess);

public class JobService : IJobService
{
    private static readonly string PromptsRoot = ResolvePromptsRoot();

    internal static readonly string SharedRoot = Path.Combine(PromptsRoot, ".shared");

    internal static string ResolvePromptsRoot()
    {
        // 1. Debug/source mode: check if Promptwares exists relative to BaseDirectory
        var sourceRoot = Path.GetFullPath(
            Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", "Promptwares"));
        if (Directory.Exists(sourceRoot))
            return sourceRoot;

        // 2. Production mode: use TENDRIL_HOME/Promptwares
        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        if (!string.IsNullOrEmpty(tendrilHome))
        {
            var deployedRoot = Path.Combine(tendrilHome, "Promptwares");
            if (Directory.Exists(deployedRoot))
                return deployedRoot;
        }

        // 3. Fallback (will fail at runtime, but gives a clear error location)
        return sourceRoot;
    }

    private static readonly Dictionary<string, string> ScriptPaths = new()
    {
        ["MakePlan"] = Path.Combine(PromptsRoot, "MakePlan", "MakePlan.ps1"),
        ["UpdatePlan"] = Path.Combine(PromptsRoot, "UpdatePlan", "UpdatePlan.ps1"),
        ["SplitPlan"] = Path.Combine(PromptsRoot, "SplitPlan", "SplitPlan.ps1"),
        ["ExpandPlan"] = Path.Combine(PromptsRoot, "ExpandPlan", "ExpandPlan.ps1"),
        ["ExecutePlan"] = Path.Combine(PromptsRoot, "ExecutePlan", "ExecutePlan.ps1"),
        ["IvyFrameworkVerification"] = Path.Combine(PromptsRoot, "IvyFrameworkVerification", "IvyFrameworkVerification.ps1"),
        ["MakePr"] = Path.Combine(PromptsRoot, "MakePr", "MakePr.ps1"),
        ["CreateIssue"] = Path.Combine(PromptsRoot, "CreateIssue", "CreateIssue.ps1")
    };

    private readonly IConfigService? _configService;
    private readonly IPlanDatabaseService? _database;

    private readonly string? _inboxPath;
    private readonly PriorityQueue<string, int> _jobQueue = new();
    private readonly object _queueLock = new();
    private readonly SemaphoreSlim _jobSlotSemaphore;
    private TimeSpan _jobTimeout;
    private readonly ConcurrentDictionary<string, JobItem> _jobs = new();
    private int _maxConcurrentJobs;
    private readonly ModelPricingService? _modelPricingService;
    private readonly IPlanReaderService? _planReaderService;
    private readonly IPlanWatcherService? _planWatcherService;
    private TimeSpan _staleOutputTimeout;
    private readonly SynchronizationContext? _syncContext;
    private readonly ITelemetryService? _telemetryService;
    private readonly IWorktreeLifecycleLogger? _worktreeLifecycleLogger;
    private readonly ILogger<JobService> _logger;
    private int _counter;

    public JobService(
        IConfigService configService,
        ILogger<JobService>? logger = null,
        ModelPricingService? modelPricingService = null,
        IPlanReaderService? planReaderService = null,
        ITelemetryService? telemetryService = null,
        IPlanWatcherService? planWatcherService = null,
        IPlanDatabaseService? database = null,
        IWorktreeLifecycleLogger? worktreeLifecycleLogger = null)
    {
        _syncContext = SynchronizationContext.Current;
        _configService = configService;
        _logger = logger ?? NullLogger<JobService>.Instance;
        _modelPricingService = modelPricingService;
        _planReaderService = planReaderService;
        _telemetryService = telemetryService;
        _planWatcherService = planWatcherService;
        _database = database;
        _worktreeLifecycleLogger = worktreeLifecycleLogger;
        _jobTimeout = TimeSpan.FromMinutes(configService.Settings.JobTimeout);
        _staleOutputTimeout = TimeSpan.FromMinutes(configService.Settings.StaleOutputTimeout);
        _maxConcurrentJobs = configService.Settings.MaxConcurrentJobs;
        _jobSlotSemaphore = _maxConcurrentJobs > 0
            ? new SemaphoreSlim(_maxConcurrentJobs, _maxConcurrentJobs)
            : new SemaphoreSlim(0, 1);
        _inboxPath = Path.Combine(configService.TendrilHome, "Inbox");
        configService.SettingsReloaded += OnSettingsReloaded;
        LoadHistoricalJobs();
    }

    public JobService(
        TimeSpan jobTimeout,
        TimeSpan staleOutputTimeout,
        string? inboxPath = null,
        int maxConcurrentJobs = 5,
        IPlanReaderService? planReaderService = null,
        ITelemetryService? telemetryService = null,
        IPlanDatabaseService? database = null,
        ILogger<JobService>? logger = null)
    {
        _syncContext = SynchronizationContext.Current;
        _logger = logger ?? NullLogger<JobService>.Instance;
        _jobTimeout = jobTimeout;
        _staleOutputTimeout = staleOutputTimeout;
        _maxConcurrentJobs = maxConcurrentJobs;
        _jobSlotSemaphore = maxConcurrentJobs > 0
            ? new SemaphoreSlim(maxConcurrentJobs, maxConcurrentJobs)
            : new SemaphoreSlim(0, 1);
        _inboxPath = inboxPath;
        _planReaderService = planReaderService;
        _telemetryService = telemetryService;
        _database = database;
    }

    public event Action? JobsChanged;
    public event Action? JobsStructureChanged;
    public event Action? JobPropertyChanged;
    public event Action<JobNotification>? NotificationReady;

    public string StartJob(string type, string[] args, string? inboxFilePath)
    {
        return StartJobInternal(type, args, inboxFilePath);
    }

    public string StartJob(string type, params string[] args)
    {
        return StartJobInternal(type, args, null);
    }

    public void CompleteJob(string id, int? exitCode, bool timedOut = false, bool staleOutput = false)
    {
        if (!_jobs.TryGetValue(id, out var job)) return;
        if (!job.TryClaimCompletion()) return;

        if (timedOut)
        {
            job.Status = JobStatus.Timeout;
            var reason = (staleOutput || job.StaleOutputDetected)
                ? $"No output for {(int)_staleOutputTimeout.TotalMinutes} minutes"
                : $"Exceeded {(int)_jobTimeout.TotalMinutes} minute timeout";
            job.StatusMessage = reason;
        }
        else
        {
            var success = exitCode == 0;
            if (!success)
                job.StatusMessage ??= ExtractFailureReason(job.OutputLines.ToList(), job.Type);
            else
                job.StatusMessage = null;
            job.Status = success ? JobStatus.Completed : JobStatus.Failed;
        }

        job.CompletedAt = DateTime.UtcNow;
        if (job.StartedAt.HasValue)
            job.DurationSeconds = (int)(job.CompletedAt.Value - job.StartedAt.Value).TotalSeconds;

        // Release job slot for next queued job
        _jobSlotSemaphore.Release();

        // Run after-hooks
        var planFolderForHooks = job.Args.Length > 0 ? job.Args[0] : "";
        RunHooks("after", job.Type, planFolderForHooks, job.Project, job);

        var isSuccess = job.Status == JobStatus.Completed;
        var title = job.Status == JobStatus.Timeout ? $"{job.Type} Timed Out" :
            isSuccess ? $"{job.Type} Completed" : $"{job.Type} Failed";
        var message = job.PlanFile ?? job.Type;
        if (!isSuccess && job.StatusMessage != null)
            message += $": {job.StatusMessage}";
        var completionNotification = new JobNotification(title, message, isSuccess);
        RaiseNotification(completionNotification);

        if (job.Status is JobStatus.Failed or JobStatus.Timeout)
        {
            ResetPlanState(job);
            ScheduleWorktreeCleanup(job);
        }
        else if (isSuccess && job.Type == "ExecutePlan")
        {
            EnsurePlanStateTransitioned(job);
        }
        else if (isSuccess && job.Type == "CreateIssue")
        {
            SetPlanState(job, "Completed");
            RetryBlockedDependents(job.Args.Length > 0 ? job.Args[0] : "");
        }
        else if (isSuccess && job.Type == "MakePlan")
        {
            VerifyMakePlanResult(job);
            if (job.Status == JobStatus.Completed)
            {
                var planFolder = job.Args.Length > 0 ? job.Args[0] : "";
                var level = "NiceToHave";
                if (Directory.Exists(planFolder))
                {
                    var plan = ReadPlanYaml(planFolder);
                    if (plan != null) level = plan.Level;
                }

                _telemetryService?.TrackPlanCreated(new PlanCreatedContext(
                    level,
                    job.DurationSeconds));
            }
        }

        if (isSuccess && job.Type == "MakePr")
        {
            _telemetryService?.TrackPrCreated(new PrCreatedContext(
                job.DurationSeconds));
            RetryBlockedDependents(job.Args.Length > 0 ? job.Args[0] : "");
        }

        _telemetryService?.TrackJobCompleted(job.Type, job.Status, job.DurationSeconds);

        // Flush telemetry events to ensure they reach PostHog
        if (_telemetryService != null)
            _ = Task.Run(async () => { try { await _telemetryService.FlushAsync(); } catch { /* best-effort */ } });

        CleanupInboxFile(job);
        WriteJobLog(job);

        // Notify plan watcher so the database sync picks up any new/changed plans
        var notifyFolder = job.Args.Length > 0 ? job.Args[0] : null;
        _planWatcherService?.NotifyChanged(Directory.Exists(notifyFolder) ? notifyFolder : null);

        // Calculate and log costs automatically (delayed to allow session to complete)
        if (isSuccess && _modelPricingService != null && !string.IsNullOrEmpty(job.SessionId))
        {
            var sessionId = job.SessionId;
            var jobArgs = job.Args;
            var jobType = job.Type;
            var jobId = job.Id;
            var provider = job.Provider;

            _ = Task.Run(async () =>
            {
                // Wait for Claude session to write final usage data
                await Task.Delay(TimeSpan.FromSeconds(30));

                try
                {
                    var costCalc = _modelPricingService.CalculateSessionCost(sessionId, provider);
                    if (costCalc.TotalCost > 0)
                    {
                        if (_jobs.TryGetValue(jobId, out var j))
                        {
                            j.Cost = (decimal)costCalc.TotalCost;
                            j.Tokens = costCalc.TotalTokens;
                            PersistJob(j);
                            RaiseJobsPropertyChanged();
                        }

                        if (jobArgs.Length > 0)
                            LogCostToCsv(jobArgs[0], jobType, costCalc.TotalTokens, costCalc.TotalCost);
                    }
                }
                catch
                {
                    /* Best-effort cost tracking */
                }
            });
        }

        // Release process and timeout resources
        job.DisposeResources();

        // Persist completed job to SQLite
        PersistJob(job);

        // Evict stale finished jobs from memory to prevent unbounded dictionary growth.
        // Job metadata is already persisted to SQLite; the in-memory copy is only needed
        // for active display and is reloaded from DB on next startup.
        EvictStaleJobs();

        RaiseJobsStructureChanged();

        // After successful completion of jobs that may unblock dependencies
        if (isSuccess && job.Type is "ExecutePlan" or "MakePr") RetryBlockedJobs();

        // Try to start queued jobs now that a slot is free
        ProcessJobQueue();

    }

    public void StopJob(string id)
    {
        if (!_jobs.TryGetValue(id, out var job)) return;
        if (!job.TryClaimCompletion()) return;

        var wasRunning = job.Status == JobStatus.Running;
        job.CancellationRequested = true;
        try
        {
            job.TimeoutCts?.Cancel();
        }
        catch
        {
            /* CTS may already be disposed */
        }

        try
        {
            job.Process?.Kill(true);
        }
        catch
        {
            /* Process may have already exited */
        }

        job.DisposeResources();

        job.Status = JobStatus.Stopped;
        job.CompletedAt = DateTime.UtcNow;
        if (job.StartedAt.HasValue)
            job.DurationSeconds = (int)(job.CompletedAt.Value - job.StartedAt.Value).TotalSeconds;

        // Release job slot if the job was running
        if (wasRunning)
            _jobSlotSemaphore.Release();

        CleanupInboxFile(job);
        ResetPlanState(job);
        RaiseJobsStructureChanged();

        // Try to start queued jobs now that a slot is free
        if (wasRunning)
            ProcessJobQueue();
    }

    public void DeleteJob(string id)
    {
        if (_jobs.TryRemove(id, out var removed))
        {
            removed.DisposeResources();
            try { _database?.DeleteJob(id); } catch { /* Best-effort */ }
        }
        RaiseJobsStructureChanged();
    }

    /// <summary>
    ///     Removes finished jobs older than 1 hour from the in-memory dictionary.
    ///     Job metadata remains in SQLite and is reloaded on next startup via LoadHistoricalJobs.
    ///     Keeps the most recent 20 finished jobs regardless of age so the UI stays useful.
    /// </summary>
    private void EvictStaleJobs()
    {
        const int keepRecent = 20;
        var cutoff = DateTime.UtcNow.AddHours(-1);

        var staleJobs = _jobs.Values
            .Where(j => j.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Timeout or JobStatus.Stopped)
            .Where(j => j.CompletedAt.HasValue && j.CompletedAt.Value < cutoff)
            .OrderByDescending(j => j.CompletedAt)
            .Skip(keepRecent)
            .Select(j => j.Id)
            .ToList();

        foreach (var id in staleJobs)
            if (_jobs.TryRemove(id, out var removed))
                removed.DisposeResources();
    }

    public void ClearCompletedJobs()
    {
        var completedIds = _jobs.Values
            .Where(j => j.Status == JobStatus.Completed)
            .Select(j => j.Id)
            .ToList();
        foreach (var id in completedIds)
        {
            if (_jobs.TryRemove(id, out var removed))
                removed.DisposeResources();
            try { _database?.DeleteJob(id); } catch { /* Best-effort */ }
        }
        if (completedIds.Count > 0)
            RaiseJobsStructureChanged();
    }

    public void ClearFailedJobs()
    {
        var failedIds = _jobs.Values
            .Where(j => j.Status is JobStatus.Failed or JobStatus.Timeout or JobStatus.Blocked)
            .Select(j => j.Id)
            .ToList();
        foreach (var id in failedIds)
        {
            if (_jobs.TryRemove(id, out var removed))
                removed.DisposeResources();
            try { _database?.DeleteJob(id); } catch { /* Best-effort */ }
        }
        if (failedIds.Count > 0)
            RaiseJobsStructureChanged();
    }

    public List<JobItem> GetJobs()
    {
        return _jobs.Values.ToArray().OrderByDescending(j => j.StartedAt ?? DateTime.MinValue).ToList();
    }

    public JobItem? GetJob(string id)
    {
        return _jobs.GetValueOrDefault(id);
    }

    /// <summary>
    ///     Checks whether the given inbox file path is already tracked by a running MakePlan job.
    ///     Used by InboxWatcherService to avoid re-processing files.
    /// </summary>
    public bool IsInboxFileTracked(string filePath)
    {
        return _jobs.Values.Any(j =>
            j.Type == "MakePlan" &&
            j.Status == JobStatus.Running &&
            j.InboxFile != null &&
            j.InboxFile.Equals(filePath, StringComparison.OrdinalIgnoreCase));
    }

    private void LoadHistoricalJobs()
    {
        if (_database == null) return;
        try
        {
            var historicalJobs = _database.GetRecentJobs();
            foreach (var job in historicalJobs) _jobs.TryAdd(job.Id, job);
        }
        catch
        {
            /* Best-effort — don't block startup */
        }
    }

    private void PersistJob(JobItem job)
    {
        try
        {
            _database?.UpsertJob(job);
        }
        catch
        {
            /* Best-effort persistence */
        }
    }

    private void RaiseJobsChanged()
    {
        if (_syncContext != null)
            _syncContext.Post(_ => JobsChanged?.Invoke(), null);
        else
            JobsChanged?.Invoke();
    }

    private void RaiseJobsStructureChanged()
    {
        if (_syncContext != null)
            _syncContext.Post(_ =>
            {
                JobsStructureChanged?.Invoke();
                JobsChanged?.Invoke();  // For backward compatibility
            }, null);
        else
        {
            JobsStructureChanged?.Invoke();
            JobsChanged?.Invoke();
        }
    }

    private void RaiseJobsPropertyChanged()
    {
        if (_syncContext != null)
            _syncContext.Post(_ =>
            {
                JobPropertyChanged?.Invoke();
                JobsChanged?.Invoke();  // For backward compatibility
            }, null);
        else
        {
            JobPropertyChanged?.Invoke();
            JobsChanged?.Invoke();
        }
    }

    private void RaiseNotification(JobNotification notification)
    {
        if (_syncContext != null)
            _syncContext.Post(_ => NotificationReady?.Invoke(notification), null);
        else
            NotificationReady?.Invoke(notification);
    }

    private string StartJobSkipDepCheck(string type, params string[] args)
    {
        return StartJobInternal(type, args, inboxFilePath: null, skipDependencyCheck: true);
    }

    private string StartJobInternal(string type, string[] args, string? inboxFilePath, bool skipDependencyCheck = false)
    {
        var id = $"job-{Interlocked.Increment(ref _counter):D3}";
        var scriptPath = ScriptPaths.GetValueOrDefault(type, "");

        // Extract plan folder and project from args
        var planFile = "";
        var project = "Auto";

        // For MakePlan: args are named params like -Description "..." -Project "..."
        // For others: args[0] is the plan folder path
        var priority = 0;
        if (type == "MakePlan")
        {
            planFile = GetNamedArg(args, "-Description") is { Length: > 0 } desc
                ? desc.Length > 50 ? desc[..50] + "..." : desc
                : "New Plan";
            project = GetNamedArg(args, "-Project") ?? "Auto";
            if (int.TryParse(GetNamedArg(args, "-Priority"), out var parsedPriority))
                priority = parsedPriority;
        }
        else
        {
            var planFolder = args.Length > 0 ? args[0] : "";
            planFile = Path.GetFileName(planFolder);
            if (Directory.Exists(planFolder))
            {
                var plan = ReadPlanYaml(planFolder);
                if (plan != null)
                {
                    project = plan.Project;
                    priority = plan.Priority;
                }
            }
        }

        var job = new JobItem
        {
            Id = id,
            Type = type,
            PlanFile = planFile,
            Project = project,
            Status = JobStatus.Pending,
            ScriptPath = scriptPath,
            Args = args,
            Provider = _configService?.Settings.CodingAgent ?? "claude",
            Priority = priority
        };

        // For MakePlan jobs: track the inbox file for crash recovery
        if (type == "MakePlan")
        {
            if (inboxFilePath != null)
                // Inbox-originated job — file already renamed to .processing by InboxWatcherService
                job.InboxFile = inboxFilePath;
            else if (_inboxPath != null)
                // Manual MakePlan — write a .processing inbox file as a write-ahead log
                try
                {
                    Directory.CreateDirectory(_inboxPath);
                    var description = GetNamedArg(args, "-Description") ?? "New Plan";
                    var inboxProject = GetNamedArg(args, "-Project") ?? "Auto";
                    var pendingFile = Path.Combine(_inboxPath, $"pending-{id}.md.processing");
                    var content = $"---\nproject: {inboxProject}\n---\n{description}";
                    FileHelper.WriteAllText(pendingFile, content);
                    job.InboxFile = pendingFile;
                }
                catch
                {
                    /* Best-effort — don't fail the job if we can't write the recovery file */
                }
        }

        _jobs[id] = job;

        // Check dependencies for ExecutePlan jobs
        if (type == "ExecutePlan" && !skipDependencyCheck)
        {
            var planFolder = args.Length > 0 ? args[0] : "";
            var (ok, blockReason) = CheckDependencies(planFolder);
            if (!ok)
            {
                job.Status = JobStatus.Blocked;
                job.StatusMessage = blockReason;
                job.CompletedAt = DateTime.UtcNow;

                // Reset plan state back to Draft since we can't execute
                ResetPlanStateToBlocked(job);

                var blockedNotification = new JobNotification("Job Blocked", $"{planFile}: {blockReason}", false);
                RaiseNotification(blockedNotification);
                RaiseJobsStructureChanged();
                return id;
            }

            // Check for concurrent execution on same repos
            var (concurrentOk, concurrentReason) = CheckRepositoryConcurrency(planFolder);
            if (!concurrentOk)
            {
                job.Status = JobStatus.Blocked;
                job.StatusMessage = concurrentReason;
                job.CompletedAt = DateTime.UtcNow;
                ResetPlanStateToBlocked(job);
                var blockedNotification = new JobNotification("Job Blocked", $"{planFile}: {concurrentReason}", false);
                RaiseNotification(blockedNotification);
                RaiseJobsStructureChanged();
                return id;
            }
        }

        // Ensure any pending plan state writes are flushed to disk before scripts read plan.yaml
        if (type is "ExecutePlan" or "ExpandPlan" or "UpdatePlan" or "SplitPlan")
            _planReaderService?.FlushPendingWritesAsync().GetAwaiter().GetResult();

        // Try to acquire a job slot
        if (!_jobSlotSemaphore.Wait(0))
        {
            job.Status = JobStatus.Queued;
            job.StatusMessage = $"Waiting (max {_maxConcurrentJobs} concurrent jobs)";
            lock (_queueLock) { _jobQueue.Enqueue(id, -job.Priority); }
            RaiseJobsStructureChanged();
            return id;
        }

        LaunchJob(job);
        return id;
    }

    /// <summary>
    ///     Creates a job in "Running" state without launching a real process.
    ///     Used by tests to exercise CompleteJob without background monitor races.
    /// </summary>
    internal string CreateTestJob(string type, params string[] args)
    {
        var id = $"job-{Interlocked.Increment(ref _counter):D3}";
        var job = new JobItem
        {
            Id = id,
            Type = type,
            PlanFile = args.Length > 0 ? args[0] : type,
            Status = JobStatus.Running,
            StartedAt = DateTime.UtcNow,
            ScriptPath = "",
            Args = args,
            TimeoutCts = new CancellationTokenSource()
        };
        _jobs[id] = job;
        _jobSlotSemaphore.Wait(0); // Acquire slot so CompleteJob can release it
        return id;
    }

    /// <summary>
    ///     Removes a job from the dictionary. Used by tests to simulate concurrent removal.
    /// </summary>
    internal bool RemoveJob(string id) => _jobs.TryRemove(id, out _);

    public void Dispose()
    {
        if (_configService != null)
            _configService.SettingsReloaded -= OnSettingsReloaded;
        _jobSlotSemaphore.Dispose();
    }

    private void OnSettingsReloaded(object? sender, EventArgs e)
    {
        if (_configService == null) return;
        _jobTimeout = TimeSpan.FromMinutes(_configService.Settings.JobTimeout);
        _staleOutputTimeout = TimeSpan.FromMinutes(_configService.Settings.StaleOutputTimeout);
        _maxConcurrentJobs = _configService.Settings.MaxConcurrentJobs;
    }

    private void LaunchJob(JobItem job)
    {
        var id = job.Id;
        var type = job.Type;
        var args = job.Args;
        var scriptPath = job.ScriptPath;

        job.Status = JobStatus.Running;
        job.StartedAt = DateTime.UtcNow;
        job.StatusMessage = null;

        // Run before-hooks
        var planFolderForHooks = type != "MakePlan" && args.Length > 0 ? args[0] : "";
        RunHooks("before", type, planFolderForHooks, job.Project, job);

        // Launch process
        var processArgs = new List<string> { "-NoProfile", "-NonInteractive", "-File", scriptPath };
        processArgs.AddRange(args);

        var workingDirectory = Path.GetFullPath(
            Path.Combine(System.AppContext.BaseDirectory, "..", "..", ".."));

        var psi = new ProcessStartInfo
        {
            FileName = "pwsh",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        // Generate session ID for cost tracking — passed to child process so both sides share the same ID
        job.SessionId = Guid.NewGuid().ToString();

        psi.Environment["TENDRIL_JOB_ID"] = id;
        psi.Environment["TENDRIL_URL"] = Environment.GetEnvironmentVariable("TENDRIL_URL") ?? "https://localhost:5010";
        psi.Environment["TENDRIL_SHARED"] = SharedRoot;
        psi.Environment["TENDRIL_SESSION_ID"] = job.SessionId;
        if (_configService != null)
            psi.Environment["TENDRIL_CONFIG"] = _configService.ConfigPath;

        foreach (var arg in processArgs)
            psi.ArgumentList.Add(arg);

        var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) =>
        {
            try
            {
                if (e.Data != null)
                {
                    job.LastOutputAt = DateTime.UtcNow;
                    if (!e.Data.Contains("\"type\":\"heartbeat\"")) job.EnqueueOutput(e.Data);
                }
            }
            catch (Exception ex)
            {
                CrashLog.Write($"[{DateTime.UtcNow:O}] OutputDataReceived exception for job {id}: {ex}");
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            try
            {
                if (e.Data != null)
                {
                    job.EnqueueOutput($"[stderr] {e.Data}");
                    job.LastOutputAt = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                CrashLog.Write($"[{DateTime.UtcNow:O}] ErrorDataReceived exception for job {id}: {ex}");
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        job.Process = process;
        job.ProcessId = process.Id;

        // Monitor for completion in background with timeout and stale output detection
        var cts = new CancellationTokenSource(_jobTimeout);
        job.TimeoutCts = cts;

        Task.Run(async () =>
        {
            _logger.LogDebug("Job {JobId}: Monitor task started", id);

            try
            {
                if (await process.WaitForExitOrKillAsync(cts.Token))
                {
                    if (_jobs.TryGetValue(id, out var j) && j.StaleOutputDetected)
                    {
                        _logger.LogInformation("Job {JobId}: Process exited, stale output detected", id);
                        CompleteJob(id, null, timedOut: true, staleOutput: true);
                    }
                    else
                    {
                        _logger.LogInformation("Job {JobId}: Process exited with code {ExitCode}", id, process.ExitCode);
                        CompleteJob(id, process.ExitCode);
                    }
                }
                else
                {
                    _logger.LogWarning("Job {JobId}: Process killed after timeout", id);
                    CompleteJob(id, null, timedOut: true);
                }

                _logger.LogDebug("Job {JobId}: Monitor task completed normally", id);
            }
            catch (ObjectDisposedException)
            {
                _logger.LogDebug("Job {JobId}: Monitor task exiting (CTS disposed, job completed elsewhere)", id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId}: Monitor task exception", id);
                CrashLog.Write($"[{DateTime.UtcNow:O}] JobService process monitor exception for job {id}: {ex}");
                CompleteJob(id, null, timedOut: false);
            }
        });

        // Start stale output watchdog
        if (_staleOutputTimeout > TimeSpan.Zero) _ = RunStaleOutputWatchdog(id, cts);

        RaiseJobsStructureChanged();
    }

    internal void RunHooks(string when, string jobType, string planFolder, string project, JobItem job)
    {
        if (_configService == null) return;

        var projectConfig = _configService.GetProject(project);
        if (projectConfig == null) return;

        var hooks = projectConfig.Hooks
            .Where(h => h.When.Equals(when, StringComparison.OrdinalIgnoreCase))
            .Where(h => h.Promptwares.Count == 0 || h.Promptwares.Contains(jobType, StringComparer.OrdinalIgnoreCase))
            .ToList();

        foreach (var hook in hooks)
            try
            {
                // Evaluate condition if set
                if (!string.IsNullOrWhiteSpace(hook.Condition))
                {
                    var condPsi = new ProcessStartInfo
                    {
                        FileName = "pwsh",
                        Arguments = $"-NoProfile -NonInteractive -EncodedCommand {EncodeForPowerShell(hook.Condition)}",
                        WorkingDirectory = string.IsNullOrEmpty(planFolder) ? "." : planFolder,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        RedirectStandardInput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var condProc = Process.Start(condPsi);
                    var condOutput = condProc?.StandardOutput.ReadToEnd().Trim() ?? "";
                    condProc.WaitForExitOrKill(10000);

                    if (condProc?.ExitCode != 0 ||
                        condOutput.Equals("False", StringComparison.OrdinalIgnoreCase))
                    {
                        job.EnqueueOutput($"[hook:{hook.Name}] Condition not met, skipping");
                        continue;
                    }
                }

                // Run the action
                var actionPsi = new ProcessStartInfo
                {
                    FileName = "pwsh",
                    Arguments = $"-NoProfile -NonInteractive -EncodedCommand {EncodeForPowerShell(hook.Action)}",
                    WorkingDirectory = string.IsNullOrEmpty(planFolder) ? "." : planFolder,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                actionPsi.Environment["TENDRIL_JOB_ID"] = job.Id;
                actionPsi.Environment["TENDRIL_JOB_TYPE"] = jobType;
                actionPsi.Environment["TENDRIL_JOB_STATUS"] = job.Status.ToString();
                actionPsi.Environment["TENDRIL_PLAN_FOLDER"] = planFolder;
                actionPsi.Environment["TENDRIL_CONFIG"] = _configService.ConfigPath;

                using var actionProc = Process.Start(actionPsi);
                var output = actionProc?.StandardOutput.ReadToEnd().Trim() ?? "";
                var stderr = actionProc?.StandardError.ReadToEnd().Trim() ?? "";
                actionProc.WaitForExitOrKill(30000);

                if (!string.IsNullOrEmpty(output))
                    job.EnqueueOutput($"[hook:{hook.Name}] {output}");
                if (!string.IsNullOrEmpty(stderr))
                    job.EnqueueOutput($"[hook:{hook.Name}] [stderr] {stderr}");

                if (actionProc?.ExitCode != 0)
                    job.EnqueueOutput($"[hook:{hook.Name}] Hook failed with exit code {actionProc?.ExitCode}");
            }
            catch (Exception ex)
            {
                job.EnqueueOutput($"[hook:{hook.Name}] Error: {ex.Message}");
            }
    }

    internal async Task RunStaleOutputWatchdog(string id, CancellationTokenSource timeoutCts)
    {
        const int checkIntervalSeconds = 60;

        try
        {
            while (!timeoutCts.Token.IsCancellationRequested)
            {
                for (var i = 0; i < checkIntervalSeconds; i++)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), timeoutCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    if (timeoutCts.Token.IsCancellationRequested) return;
                }

                if (!_jobs.TryGetValue(id, out var job) || job.Status != JobStatus.Running)
                    break;

                if (job.LastOutputAt.HasValue)
                {
                    var sinceLastOutput = DateTime.UtcNow - job.LastOutputAt.Value;
                    if (sinceLastOutput >= _staleOutputTimeout)
                    {
                        job.StaleOutputDetected = true;
                        try { timeoutCts.Cancel(); } catch (ObjectDisposedException) { }
                        break;
                    }
                }
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void RetryBlockedJobs()
    {
        var blockedJobs = _jobs.Values
            .Where(j => j.Status == JobStatus.Blocked && j.Type == "ExecutePlan")
            .ToList();

        foreach (var blockedJob in blockedJobs)
        {
            var planFolder = blockedJob.Args.Length > 0 ? blockedJob.Args[0] : "";
            if (string.IsNullOrEmpty(planFolder)) continue;

            var (ok, _) = CheckDependencies(planFolder);
            if (!ok)
                continue;

            // Also check concurrency before retrying
            var (concurrentOk, _) = CheckRepositoryConcurrency(planFolder);
            if (!concurrentOk)
                continue;

            // Remove the blocked job entry — only one thread succeeds
            if (!_jobs.TryRemove(blockedJob.Id, out _))
                continue; // Another thread already handled this job

            // Skip if there's already an active job for this plan
            if (HasActiveJobForPlan(planFolder))
                continue;

            // Transition plan to Building before re-launching (ExecutePlan.ps1 requires it)
            SetPlanStateByFolder(planFolder, "Building");

            // Re-start the job — skip dependency check since we just verified
            StartJobSkipDepCheck(blockedJob.Type, blockedJob.Args);

            var notification = new JobNotification(
                "Job Unblocked",
                $"{blockedJob.PlanFile}: dependencies now satisfied, auto-restarting",
                true);
            RaiseNotification(notification);
        }
    }

    private bool HasActiveJobForPlan(string planFolder)
    {
        return _jobs.Values.Any(j =>
            j.Type == "ExecutePlan" &&
            j.Status is JobStatus.Running or JobStatus.Queued or JobStatus.Pending &&
            j.Args.Length > 0 &&
            j.Args[0].Equals(planFolder, StringComparison.OrdinalIgnoreCase));
    }

    private void ProcessJobQueue()
    {
        while (true)
        {
            if (!_jobSlotSemaphore.Wait(0))
                break;

            string? queuedId;
            lock (_queueLock)
            {
                if (!_jobQueue.TryDequeue(out queuedId, out _))
                {
                    _jobSlotSemaphore.Release();
                    break;
                }
            }

            if (!_jobs.TryGetValue(queuedId, out var queuedJob) || queuedJob.Status != JobStatus.Queued)
            {
                _jobSlotSemaphore.Release();
                continue;
            }

            LaunchJob(queuedJob);
        }
    }

    private static string EncodeForPowerShell(string command)
    {
        var bytes = Encoding.Unicode.GetBytes(command);
        return Convert.ToBase64String(bytes);
    }

    internal static string ExtractFailureReason(List<string> outputLines, string jobType)
    {
        if (outputLines.Count == 0)
            return "Unknown error (exit code non-zero)";

        // 1. Check for PowerShell terminating errors
        var psError = FindPattern(outputLines, new[] {
            @"At line:\d+",
            @"CategoryInfo\s+:",
            @"TerminatingError\(",
            @"ScriptHalted",
            @"^\s*\+\s+CategoryInfo",  // PowerShell error location marker
        });
        if (psError != null) return psError;

        // 2. Check for Claude API errors (from JSON stream)
        var apiError = FindPattern(outputLines, new[] {
            @"""type"":\s*""error""",
            @"""error"":\s*\{",
            @"rate_limit_error",
            @"overloaded_error",
            @"authentication_error",
        });
        if (apiError != null) return ParseClaudeApiError(apiError);

        // 3. Check for MakePlan-specific failures and failure artifacts
        if (jobType == "MakePlan")
        {
            var makePlanError = FindPattern(outputLines, new[] {
                @"ERROR: Plan",
                @"WARNING: TENDRIL_HOME",
                @"Failed to parse",
                @"Repository path does not exist",
            });
            if (makePlanError != null) return makePlanError;

            // Check for failure artifact
            var failureArtifactMessage = TryReadFailureArtifact(outputLines);
            if (failureArtifactMessage != null) return failureArtifactMessage;
        }

        // 4. Check for validation/assertion failures (from MakePlan/ExecutePlan)
        var validationError = FindPattern(outputLines, new[] {
            @"validation\s+failed",
            @"assertion\s+failed",
            @"Repository path does not exist",
            @"\[stderr\].*ERROR:",
        });
        if (validationError != null) return validationError;

        // 5. Search from end for stderr lines (existing logic)
        var stderrLines = new List<string>();
        for (var i = outputLines.Count - 1; i >= 0 && stderrLines.Count < 3; i--)
        {
            var line = outputLines[i];
            if (line.StartsWith("[stderr] "))
            {
                var content = line["[stderr] ".Length..].Trim();
                if (content.Length > 0 && !IsProgressMessage(content))
                    stderrLines.Insert(0, content);
            }
        }

        if (stderrLines.Count > 0)
            return SanitizeForDisplay(string.Join(" | ", stderrLines));

        // 6. Fall back to last non-progress line
        for (var i = outputLines.Count - 1; i >= 0; i--)
        {
            var trimmed = outputLines[i].Trim();
            if (trimmed.Length > 0 && !IsProgressMessage(trimmed))
                return SanitizeForDisplay(trimmed);
        }

        return "Unknown error (exit code non-zero)";
    }

    private static bool IsProgressMessage(string line)
    {
        // Filter out common progress indicators that aren't errors
        var progressPatterns = new[] {
            "Creating Plan", "Executing Plan", "Building", "Researching",
            "Reading", "Analyzing", "Writing", "Searching",
            @"^\d+%", @"^\[.*\]\s+(Starting|Running|Completed)",
        };

        return progressPatterns.Any(p => Regex.IsMatch(line, p, RegexOptions.IgnoreCase));
    }

    private static string? FindPattern(List<string> lines, string[] patterns)
    {
        for (var i = lines.Count - 1; i >= Math.Max(0, lines.Count - 50); i--)
        {
            foreach (var pattern in patterns)
            {
                if (Regex.IsMatch(lines[i], pattern, RegexOptions.IgnoreCase))
                {
                    // Return context: 2 lines before + matching line + 1 line after
                    var start = Math.Max(0, i - 2);
                    var end = Math.Min(lines.Count - 1, i + 1);
                    var context = string.Join(" | ", lines.Skip(start).Take(end - start + 1));
                    return SanitizeForDisplay(context);
                }
            }
        }
        return null;
    }

    private static string ParseClaudeApiError(string jsonErrorLine)
    {
        try
        {
            // Extract error message from Claude API JSON response
            var match = Regex.Match(jsonErrorLine, @"""message"":\s*""([^""]+)""");
            if (match.Success)
                return $"Claude API: {match.Groups[1].Value}";
        }
        catch { }

        return "Claude API error (see output for details)";
    }

    private static string? TryReadFailureArtifact(List<string> outputLines)
    {
        try
        {
            // Look for failure artifact path in output (from Get-FailureArtifact.ps1)
            var artifactLine = outputLines.FirstOrDefault(line =>
                line.Contains("Failure artifact written:") && line.Contains("Failed"));

            if (artifactLine == null) return null;

            // Extract path from the line: "Failure artifact written: <path>"
            var pathMatch = Regex.Match(artifactLine, @"Failure artifact written:\s*(.+)");
            if (!pathMatch.Success) return null;

            var artifactPath = pathMatch.Groups[1].Value.Trim();
            if (!File.Exists(artifactPath)) return null;

            // Read the failure artifact and extract error summary
            var lines = File.ReadAllLines(artifactPath);
            var errorOutputSection = false;
            var errorLines = new List<string>();

            foreach (var line in lines)
            {
                if (line.StartsWith("## Error Output"))
                {
                    errorOutputSection = true;
                    continue;
                }
                if (line.StartsWith("## Investigation Steps"))
                {
                    break;
                }
                if (errorOutputSection && !line.StartsWith("```"))
                {
                    errorLines.Add(line.Trim());
                }
            }

            if (errorLines.Count > 0)
            {
                var summary = string.Join(" | ", errorLines.Take(3).Where(l => l.Length > 0));
                return string.IsNullOrWhiteSpace(summary) ? null : SanitizeForDisplay(summary);
            }
        }
        catch { }

        return null;
    }

    internal static string SanitizeForDisplay(string text)
    {
        // Strip ANSI escape codes (color, cursor, formatting)
        text = Regex.Replace(text, @"\x1B\[[0-9;]*[A-Za-z]", "");

        // Replace control characters (tabs, newlines, carriage returns, null bytes, etc.) with spaces
        text = Regex.Replace(text, @"[\x00-\x1F]", " ");

        // Collapse multiple consecutive spaces into one
        text = Regex.Replace(text, @" {2,}", " ");

        return text.Trim();
    }

    internal static string? ReadPlanYamlRaw(string planFolder)
    {
        var planYamlPath = Path.Combine(planFolder, "plan.yaml");
        return File.Exists(planYamlPath) ? FileHelper.ReadAllText(planYamlPath) : null;
    }

    internal static PlanYaml? ReadPlanYaml(string planFolder)
    {
        var yaml = ReadPlanYamlRaw(planFolder);
        if (yaml == null) return null;

        try
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            return deserializer.Deserialize<PlanYaml>(yaml);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    ///     Updates one or more fields in plan.yaml using Regex replacement.
    ///     Preserves YAML formatting and field order.
    /// </summary>
    internal static void UpdatePlanYamlFields(string planFolder, params (string field, string value)[] updates)
    {
        var content = ReadPlanYamlRaw(planFolder);
        if (content == null) return;

        foreach (var (field, value) in updates)
        {
            var pattern = $@"(?m)^{Regex.Escape(field)}:\s*.*$";
            var replacement = $"{field}: {value}";
            content = Regex.Replace(content, pattern, replacement);
        }

        var planYamlPath = Path.Combine(planFolder, "plan.yaml");
        FileHelper.WriteAllText(planYamlPath, content);
    }

    /// <summary>
    ///     Updates the state and updated timestamp in plan.yaml.
    /// </summary>
    internal static void SetPlanStateByFolder(string planFolder, string state)
    {
        UpdatePlanYamlFields(planFolder,
            ("state", state),
            ("updated", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")));
    }

    private static string? GetNamedArg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    private void EnsurePlanStateTransitioned(JobItem job)
    {
        try
        {
            var planFolder = job.Args.Length > 0 ? job.Args[0] : "";
            var planYaml = ReadPlanYaml(planFolder);
            if (planYaml == null) return;

            if (planYaml.State is "Executing" or "Building")
            {
                var hasIncomplete = planYaml.Verifications?
                    .Any(v => v.Status is "Pending" or "Fail") ?? false;
                var targetState = hasIncomplete ? "Failed" : "ReadyForReview";

                SetPlanStateByFolder(planFolder, targetState);
            }
        }
        catch
        {
            /* Don't let state transition failures crash job completion */
        }
    }

    private void SetPlanState(JobItem job, string state)
    {
        try
        {
            var planFolder = job.Args.Length > 0 ? job.Args[0] : "";
            SetPlanStateByFolder(planFolder, state);
        }
        catch
        {
            /* Don't let state transition failures crash job completion */
        }
    }

    private void VerifyMakePlanResult(JobItem job)
    {
        try
        {
            if (_planReaderService == null) return;
            var plansDir = _planReaderService.PlansDirectory;
            if (!Directory.Exists(plansDir)) return;

            var outputText = string.Join("\n", job.OutputLines);
            var createdMatch = Regex.Match(outputText, @"Plan created:\s*(\S+)");
            var duplicate = Regex.IsMatch(outputText, @"identified as duplicate:");

            if (createdMatch.Success)
            {
                job.PlanFile = createdMatch.Groups[1].Value;
            }
            else if (!duplicate)
            {
                // Agent exited 0 but didn't create a plan or detect a duplicate — flag it
                job.EnqueueOutput(
                    "[Tendril] WARNING: MakePlan completed but no plan folder or trash entry was found.");
                job.Status = JobStatus.Failed;

                // Check for failure artifact to provide better diagnostics
                var failureMessage = TryReadFailureArtifact(job.OutputLines.ToList());
                job.StatusMessage = failureMessage ?? "No plan created";
            }
        }
        catch
        {
            /* Don't let verification failures crash job completion */
        }
    }

    private void ResetPlanState(JobItem job)
    {
        try
        {
            if (job.Type is "MakePlan" or "MakePr" or "CreateIssue") return;

            var planFolder = job.Args.Length > 0 ? job.Args[0] : "";
            var newState = job.Type == "ExecutePlan" ? "Failed" : "Draft";
            SetPlanStateByFolder(planFolder, newState);
        }
        catch
        {
            /* Don't let state reset failures crash job completion */
        }
    }

    private void ScheduleWorktreeCleanup(JobItem job)
    {
        if (job.Type != "ExecutePlan") return;

        var planFolder = job.Args.Length > 0 ? job.Args[0] : "";
        if (string.IsNullOrEmpty(planFolder) || !Directory.Exists(planFolder)) return;

        var worktreesDir = Path.Combine(planFolder, "worktrees");
        if (!Directory.Exists(worktreesDir)) return;

        var lifecycleLogger = _worktreeLifecycleLogger;

        Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            try
            {
                PlanReaderService.RemoveWorktrees(planFolder, lifecycleLogger: lifecycleLogger);

                if (Directory.Exists(worktreesDir) && Directory.GetDirectories(worktreesDir).Length == 0)
                    Directory.Delete(worktreesDir, false);
            }
            catch
            {
                // Best-effort: background cleanup service will catch it later
            }
        });
    }

    private (bool Ok, string? BlockReason) CheckDependencies(string planFolder)
    {
        try
        {
            var planYaml = ReadPlanYaml(planFolder);
            if (planYaml?.DependsOn == null || planYaml.DependsOn.Count == 0)
                return (true, null);

            var plansDir = _planReaderService?.PlansDirectory;
            if (plansDir == null) return (true, null);

            foreach (var dep in planYaml.DependsOn)
            {
                var depFolder = Path.Combine(plansDir, dep);
                var depPlan = ReadPlanYaml(depFolder);

                if (depPlan == null)
                    return (false, $"Dependency '{dep}' not found");

                if (!depPlan.State.Equals("Completed", StringComparison.OrdinalIgnoreCase))
                    return (false, $"Dependency '{dep}' is '{depPlan.State}', not Completed");

                // Check that all PRs are actually merged
                if (depPlan.Prs.Count == 0)
                    continue;

                foreach (var prUrl in depPlan.Prs.Where(PullRequestApp.IsValidUrl))
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "gh",
                            Arguments = $"pr view \"{prUrl}\" --json state -q .state",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var proc = Process.Start(psi);
                        var output = proc?.StandardOutput.ReadToEnd().Trim() ?? "";
                        proc.WaitForExitOrKill(10000);

                        if (!output.Equals("MERGED", StringComparison.OrdinalIgnoreCase))
                            return (false, $"Dependency '{dep}' PR {prUrl} is '{output}', not MERGED");
                    }
                    catch (Exception ex)
                    {
                        return (false, $"Failed to check PR status for '{dep}': {ex.Message}");
                    }
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Dependency check failed: {ex.Message}");
        }
    }

    private (bool Ok, string? BlockReason) CheckRepositoryConcurrency(string planFolder)
    {
        try
        {
            var planYaml = ReadPlanYaml(planFolder);
            if (planYaml == null)
                return (true, null);

            var repos = planYaml.Repos;
            if (repos.Count == 0 && _configService != null)
            {
                var projectConfig = _configService.GetProject(planYaml.Project);
                if (projectConfig != null)
                    repos = projectConfig.RepoPaths;
            }

            if (repos.Count == 0)
                return (true, null);

            var activeJobs = _jobs.Values
                .Where(j => j.Type == "ExecutePlan" &&
                           j.Status is JobStatus.Running or JobStatus.Queued or JobStatus.Pending)
                .ToList();

            foreach (var activeJob in activeJobs)
            {
                var activePlanFolder = activeJob.Args.Length > 0 ? activeJob.Args[0] : "";
                if (string.IsNullOrEmpty(activePlanFolder) || activePlanFolder == planFolder)
                    continue;

                var activePlan = ReadPlanYaml(activePlanFolder);
                if (activePlan == null) continue;

                var activeRepos = activePlan.Repos;
                if (activeRepos.Count == 0 && _configService != null)
                {
                    var activeProjectConfig = _configService.GetProject(activePlan.Project);
                    if (activeProjectConfig != null)
                        activeRepos = activeProjectConfig.RepoPaths;
                }

                foreach (var repo in repos)
                {
                    var normalizedRepo = NormalizeRepoPath(repo);
                    foreach (var activeRepo in activeRepos)
                    {
                        var normalizedActiveRepo = NormalizeRepoPath(activeRepo);
                        if (normalizedRepo == normalizedActiveRepo)
                        {
                            var activePlanId = Path.GetFileName(activePlanFolder);
                            var repoName = Path.GetFileName(repo);
                            return (false, $"Plan {activePlanId} is currently executing on repository '{repoName}'. Wait for it to complete to avoid conflicting changes.");
                        }
                    }
                }
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Concurrency check failed for plan {PlanFolder}", planFolder);
            return (false, $"Concurrency check failed: {ex.Message}");
        }
    }

    private static string NormalizeRepoPath(string path)
    {
        return path.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
    }

    private void RetryBlockedDependents(string completedPlanFolder)
    {
        try
        {
            var completedFolderName = Path.GetFileName(completedPlanFolder);
            var plansDir = _planReaderService?.PlansDirectory;
            if (string.IsNullOrEmpty(plansDir) || !Directory.Exists(plansDir)) return;

            foreach (var dir in Directory.GetDirectories(plansDir))
            {
                var planYaml = ReadPlanYaml(dir);
                if (planYaml == null) continue;
                if (!planYaml.State.Equals("Blocked", StringComparison.OrdinalIgnoreCase)) continue;
                if (!planYaml.DependsOn.Contains(completedFolderName, StringComparer.OrdinalIgnoreCase)) continue;

                // Skip if there's already a blocked or active job for this plan
                var hasExistingJob = _jobs.Values.Any(j =>
                    j.Type == "ExecutePlan" &&
                    j.Status is JobStatus.Blocked or JobStatus.Running or JobStatus.Queued or JobStatus.Pending &&
                    j.Args.Length > 0 &&
                    j.Args[0].Equals(dir, StringComparison.OrdinalIgnoreCase));
                if (hasExistingJob) continue;

                var (allMet, _) = CheckDependencies(dir);
                if (allMet)
                {
                    SetPlanStateByFolder(dir, "Building");
                    StartJobSkipDepCheck("ExecutePlan", dir);
                }
            }
        }
        catch
        {
            /* Don't let auto-retry failures crash job completion */
        }
    }

    private void CleanupInboxFile(JobItem job)
    {
        if (string.IsNullOrEmpty(job.InboxFile)) return;
        try
        {
            if (File.Exists(job.InboxFile))
                File.Delete(job.InboxFile);
        }
        catch
        {
            /* Best-effort cleanup */
        }
    }

    private void ResetPlanStateToBlocked(JobItem job)
    {
        try
        {
            var planFolder = job.Args.Length > 0 ? job.Args[0] : "";
            SetPlanStateByFolder(planFolder, "Blocked");
        }
        catch
        {
            /* Don't let state reset failures crash job completion */
        }
    }


    internal static void LogCostToCsv(string planFolder, string jobType, int tokens, double cost)
    {
        if (!Directory.Exists(planFolder)) return;

        var csvPath = Path.Combine(planFolder, "costs.csv");
        if (!File.Exists(csvPath)) FileHelper.WriteAllText(csvPath, "Promptware,Tokens,Cost\n");

        var line = $"{jobType},{tokens},{cost:F4}\n";
        FileHelper.AppendAllText(csvPath, line);
    }

    internal void WriteJobLog(JobItem job)
    {
        if (_planReaderService == null || string.IsNullOrEmpty(job.PlanFile))
            return;

        // MakePlan jobs use the description as PlanFile (no folder exists yet) —
        // the agent writes its own logs inside the properly-named plan folder.
        if (job.Type == "MakePlan")
            return;

        try
        {
            var duration = job.DurationSeconds.HasValue ? $"{job.DurationSeconds}s" : "unknown";
            var logContent = $"# {job.Type}\n\n" +
                             $"- **Status:** {job.Status}\n" +
                             $"- **Started:** {job.StartedAt:u}\n" +
                             $"- **Completed:** {job.CompletedAt:u}\n" +
                             $"- **Duration:** {duration}\n";

            if (!string.IsNullOrEmpty(job.SessionId))
                logContent += $"- **SessionId:** {job.SessionId}\n";

            if (job.Status == JobStatus.Timeout && job.StatusMessage != null)
                logContent += $"- **Timeout Reason:** {job.StatusMessage}\n";

            _planReaderService.AddLog(job.PlanFile, job.Type, logContent);

            // Persist raw output for failed/timeout jobs (INCLUDING MakePlan)
            if (job.Status is JobStatus.Failed or JobStatus.Timeout && job.OutputLines.Count > 0)
            {
                var planFolder = job.Args.Length > 0 ? job.Args[0] : null;

                // For MakePlan jobs without a plan folder yet, use TENDRIL_HOME/Logs
                if (string.IsNullOrEmpty(planFolder) || !Directory.Exists(planFolder))
                {
                    var logRoot = Path.Combine(
                        Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? ".",
                        "Logs", "Jobs");
                    Directory.CreateDirectory(logRoot);
                    planFolder = logRoot;
                }

                var logsDir = Path.Combine(planFolder, "logs");
                Directory.CreateDirectory(logsDir);
                var outputFile = Path.Combine(logsDir, $"{job.Type}-{job.Id}.output.log");
                File.WriteAllLines(outputFile, job.OutputLines);

                job.EnqueueOutput($"[Tendril] Full output saved to: {outputFile}");
            }
        }
        catch
        {
            // Don't let log writing failures crash the job completion
        }
    }
}