using System.Collections.Concurrent;
using Ivy.Tendril.Apps.Jobs;
using Ivy.Tendril.Apps.Plans;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Services;

public record JobNotification(string Title, string Message, bool IsSuccess);

public class JobService : IJobService
{
    private static readonly string PromptsRoot = ResolvePromptsRoot();

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

    private readonly IConfigService? _configService;
    private readonly IPlanDatabaseService? _database;

    private readonly string? _inboxPath;
    private readonly PriorityQueue<string, int> _jobQueue = new();
    private readonly object _queueLock = new();
    private SemaphoreSlim _jobSlotSemaphore;
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
    private readonly JobLauncher _jobLauncher;
    private readonly JobCompletionHandler _completionHandler;
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
        _jobLauncher = new JobLauncher(configService, _logger, PromptsRoot);
        _completionHandler = new JobCompletionHandler(
            configService, _logger, modelPricingService, planReaderService,
            telemetryService, planWatcherService, worktreeLifecycleLogger, PromptsRoot);
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
        _jobLauncher = new JobLauncher(null, _logger, PromptsRoot);
        _completionHandler = new JobCompletionHandler(
            null, _logger, null, planReaderService, telemetryService,
            null, null, PromptsRoot);
        LoadHistoricalJobs();
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

        var wasRunning = job.Status == JobStatus.Running;
        SetCompletionStatus(job, exitCode, timedOut, staleOutput);
        if (wasRunning)
        {
            try
            {
                _jobSlotSemaphore.Release();
            }
            catch (SemaphoreFullException)
            {
                // Semaphore is already at max capacity (can happen if MaxConcurrentJobs
                // was decreased while jobs were running). Silently ignore.
            }
        }

        _completionHandler.HandleCompletion(
            job, _jobs, PersistJob, RaiseNotification, RaiseJobsPropertyChanged, StartJobSkipDepCheck);

        job.DisposeResources(_logger);
        PersistJob(job);
        EvictStaleJobs();
        RaiseJobsStructureChanged();
        ProcessJobQueue();
    }

    private void SetCompletionStatus(JobItem job, int? exitCode, bool timedOut, bool staleOutput)
    {
        if (timedOut)
        {
            job.Status = JobStatus.Timeout;
            job.StatusMessage = (staleOutput || job.StaleOutputDetected)
                ? $"No output for {(int)_staleOutputTimeout.TotalMinutes} minutes"
                : $"Exceeded {(int)_jobTimeout.TotalMinutes} minute timeout";
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

        job.DisposeResources(_logger);

        job.Status = JobStatus.Stopped;
        job.CompletedAt = DateTime.UtcNow;
        if (job.StartedAt.HasValue)
            job.DurationSeconds = (int)(job.CompletedAt.Value - job.StartedAt.Value).TotalSeconds;

        // Release job slot if the job was running
        if (wasRunning)
            _jobSlotSemaphore.Release();

        JobCompletionHandler.CleanupInboxFile(job);
        _completionHandler.ResetPlanState(job);

        if (job.Type is Constants.JobTypes.ExecutePlan or Constants.JobTypes.CreatePr)
            _completionHandler.HandleRetryBlockedJobs(_jobs, RaiseNotification, StartJobSkipDepCheck);

        RaiseJobsStructureChanged();

        // Try to start queued jobs now that a slot is free
        if (wasRunning)
            ProcessJobQueue();
    }

    public void DeleteJob(string id)
    {
        if (_jobs.TryRemove(id, out var removed))
        {
            removed.DisposeResources(_logger);
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
                removed.DisposeResources(_logger);
    }

    public void ClearCompletedJobs()
        => ClearJobsByStatus(j => j.Status == JobStatus.Completed);

    public void ClearFailedJobs()
        => ClearJobsByStatus(j => j.Status is JobStatus.Failed or JobStatus.Timeout);

    private void ClearJobsByStatus(Func<JobItem, bool> predicate)
    {
        var ids = _jobs.Values.Where(predicate).Select(j => j.Id).ToList();
        foreach (var id in ids)
        {
            if (_jobs.TryRemove(id, out var removed))
                removed.DisposeResources(_logger);
            try { _database?.DeleteJob(id); } catch { /* Best-effort */ }
        }
        if (ids.Count > 0)
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
    ///     Checks whether the given inbox file path is already tracked by a
    ///     pending, queued, or running CreatePlan job. Used by InboxWatcherService
    ///     to avoid spawning duplicate CreatePlan jobs for the same inbox file.
    /// </summary>
    public bool IsInboxFileTracked(string filePath)
    {
        return _jobs.Values.Any(j =>
            j.Type == Constants.JobTypes.CreatePlan &&
            j.Status is JobStatus.Pending or JobStatus.Queued or JobStatus.Running &&
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
        var job = BuildJobItem(id, type, args, inboxFilePath);

        if (TryRejectConflictingJob(job))
            return id;

        _jobs[id] = job;

        if (TryBlockForDependencies(job, skipDependencyCheck))
            return id;

        if (type is Constants.JobTypes.ExecutePlan or Constants.JobTypes.ExpandPlan or Constants.JobTypes.UpdatePlan or Constants.JobTypes.SplitPlan)
            _planReaderService?.FlushPendingWritesAsync().GetAwaiter().GetResult();

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

    private JobItem BuildJobItem(string id, string type, string[] args, string? inboxFilePath)
    {
        var (planFile, project, priority) = ExtractJobMetadata(type, args);

        var job = new JobItem
        {
            Id = id,
            Type = type,
            PlanFile = planFile,
            Project = project,
            Status = JobStatus.Pending,
            Args = args,
            Provider = _configService?.Settings.CodingAgent ?? "claude",
            Priority = priority
        };

        if (type == Constants.JobTypes.CreatePlan)
            SetupInboxTracking(job, id, args, inboxFilePath);

        return job;
    }

    private static (string PlanFile, string Project, int Priority) ExtractJobMetadata(string type, string[] args)
    {
        if (type == Constants.JobTypes.CreatePlan)
        {
            var planFile = GetNamedArg(args, "-Description") is { Length: > 0 } desc
                ? desc.Length > 50 ? desc[..50] + "..." : desc
                : "New Plan";
            var project = GetNamedArg(args, "-Project") ?? "Auto";
            int.TryParse(GetNamedArg(args, "-Priority"), out var priority);
            return (planFile, project, priority);
        }

        var folder = args.Length > 0 ? args[0] : "";
        var file = Path.GetFileName(folder);
        if (!Directory.Exists(folder))
            return (file, "Auto", 0);

        var plan = ReadPlanYaml(folder);
        return plan != null
            ? (file, plan.Project, plan.Priority)
            : (file, "Auto", 0);
    }

    private void SetupInboxTracking(JobItem job, string id, string[] args, string? inboxFilePath)
    {
        if (inboxFilePath != null)
        {
            job.InboxFile = inboxFilePath;
        }
        else if (_inboxPath != null)
        {
            try
            {
                FileHelper.EnsureDirectory(_inboxPath);
                var description = GetNamedArg(args, "-Description") ?? "New Plan";
                var inboxProject = GetNamedArg(args, "-Project") ?? "Auto";
                var pendingFile = Path.Combine(_inboxPath, $"pending-{id}.md.processing");
                var content = $"---\nproject: {inboxProject}\n---\n{description}";
                FileHelper.WriteAllText(pendingFile, content);
                job.InboxFile = pendingFile;
            }
            catch { /* Best-effort */ }
        }
    }

    private bool TryRejectConflictingJob(JobItem job)
    {
        if (job.Type is not (Constants.JobTypes.ExecutePlan or Constants.JobTypes.UpdatePlan or Constants.JobTypes.ExpandPlan or Constants.JobTypes.SplitPlan))
            return false;

        var planFolder = job.Args.Length > 0 ? job.Args[0] : "";
        var conflictingJob = _jobs.Values.FirstOrDefault(j =>
            j.Type == job.Type &&
            j.Status is JobStatus.Running or JobStatus.Queued or JobStatus.Pending &&
            j.Args.Length > 0 &&
            j.Args[0].Equals(planFolder, StringComparison.OrdinalIgnoreCase));

        if (conflictingJob == null)
            return false;

        job.Status = JobStatus.Failed;
        job.StatusMessage = $"{job.Type} already in progress for this plan (job {conflictingJob.Id})";
        job.CompletedAt = DateTime.UtcNow;
        _jobs[job.Id] = job;

        RaiseNotification(new JobNotification(
            $"{job.Type} Already Running",
            $"{job.PlanFile}: Cannot start {job.Type} while another is in progress",
            false));
        RaiseJobsStructureChanged();
        return true;
    }

    private bool TryBlockForDependencies(JobItem job, bool skipDependencyCheck)
    {
        if (job.Type != Constants.JobTypes.ExecutePlan || skipDependencyCheck)
            return false;

        var planFolder = job.Args.Length > 0 ? job.Args[0] : "";
        var (ok, blockReason) = CheckDependencies(planFolder);
        if (ok)
            return false;

        job.Status = JobStatus.Blocked;
        job.StatusMessage = blockReason;
        job.CompletedAt = DateTime.UtcNow;
        ResetPlanStateToBlocked(job);

        RaiseNotification(new JobNotification("Job Blocked", $"{job.PlanFile}: {blockReason}", false));
        RaiseJobsStructureChanged();
        return true;
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

        var newMaxConcurrent = _configService.Settings.MaxConcurrentJobs;
        if (newMaxConcurrent != _maxConcurrentJobs)
        {
            var oldSemaphore = _jobSlotSemaphore;
            var runningCount = _jobs.Values.Count(j => j.Status == JobStatus.Running);
            var availableSlots = Math.Max(0, newMaxConcurrent - runningCount);

            // Create new semaphore with correct capacity
            var newSemaphore = newMaxConcurrent > 0
                ? new SemaphoreSlim(availableSlots, newMaxConcurrent)
                : new SemaphoreSlim(0, 1);

            // Replace semaphore (field assignment is atomic)
            _jobSlotSemaphore = newSemaphore;
            _maxConcurrentJobs = newMaxConcurrent;

            // Dispose old semaphore
            oldSemaphore.Dispose();

            // Process queue in case new capacity allows more jobs to run
            if (newMaxConcurrent > runningCount)
                ProcessJobQueue();
        }
    }

    private void LaunchJob(JobItem job)
    {
        _jobLauncher.LaunchJob(
            job, _jobs, _jobSlotSemaphore, _jobTimeout, _staleOutputTimeout,
            (when, type, folder, project, j) => RunHooks(when, type, folder, project, j),
            (id, exitCode, timedOut, staleOutput) => CompleteJob(id, exitCode, timedOut, staleOutput),
            RaiseJobsStructureChanged);
    }

    internal void RunHooks(string when, string jobType, string planFolder, string project, JobItem job)
        => _completionHandler.RunHooks(when, jobType, planFolder, project, job);

    internal Task RunStaleOutputWatchdog(string id, CancellationTokenSource timeoutCts)
        => _jobLauncher.RunStaleOutputWatchdog(id, timeoutCts, _jobs, _staleOutputTimeout);


    private void ProcessJobQueue()
    {
        while (true)
        {
            if (!_jobSlotSemaphore.Wait(0))
                break;

            JobItem? queuedJob = null;
            lock (_queueLock)
            {
                if (!_jobQueue.TryDequeue(out var queuedId, out _))
                {
                    _jobSlotSemaphore.Release();
                    break;
                }

                // Check status INSIDE the lock, immediately after dequeue
                if (!_jobs.TryGetValue(queuedId, out queuedJob) || queuedJob.Status != JobStatus.Queued)
                {
                    _jobSlotSemaphore.Release();
                    continue;
                }
            }

            // Launch outside the lock (launching is expensive)
            LaunchJob(queuedJob);
        }
    }


    internal static string ExtractFailureReason(List<string> outputLines, string jobType)
        => JobFailureAnalyzer.ExtractFailureReason(outputLines, jobType);

    internal static string SanitizeForDisplay(string text)
        => JobFailureAnalyzer.SanitizeForDisplay(text);

    internal static string? ReadPlanYamlRaw(string planFolder)
        => PlanYamlHelper.ReadPlanYamlRaw(planFolder);

    internal static PlanYaml? ReadPlanYaml(string planFolder)
        => PlanYamlHelper.ReadPlanYaml(planFolder);

    internal static void UpdatePlanYamlFields(string planFolder, params (string field, string value)[] updates)
        => PlanYamlHelper.UpdatePlanYamlFields(planFolder, updates);

    internal static void SetPlanStateByFolder(string planFolder, string state)
        => PlanYamlHelper.SetPlanStateByFolder(planFolder, state);

    private static string? GetNamedArg(string[] args, string name)
        => PlanYamlHelper.GetNamedArg(args, name);

    private (bool Ok, string? BlockReason) CheckDependencies(string planFolder)
        => _completionHandler.CheckDependencies(planFolder);

    private void ResetPlanStateToBlocked(JobItem job)
        => _completionHandler.ResetPlanStateToBlocked(job);

    internal void WriteJobLog(JobItem job)
        => _completionHandler.WriteJobLog(job);

    internal static void LogCostToCsv(string planFolder, string jobType, int tokens, double cost)
        => PlanYamlHelper.LogCostToCsv(planFolder, jobType, tokens, cost);
}