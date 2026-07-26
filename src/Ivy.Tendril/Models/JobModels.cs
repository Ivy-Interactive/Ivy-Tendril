using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reactive.Subjects;
using System.Text.Json.Serialization;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Claude;
using Ivy.Tendril.Agents.Runtime;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Models;

public enum JobStatus
{
    Pending,
    Queued,
    Running,
    Completed,
    Failed,
    Timeout,
    Stopped,
    Blocked
}

public record JobItem
{
    /// <summary>
    /// Maximum number of output lines retained per job <em>in memory</em> during execution.
    /// Lines beyond this limit are dropped from the queue, not from disk: the eventwire file under
    /// &lt;TendrilHome&gt;/Jobs/ is appended to as events are produced and keeps the complete stream.
    /// Memory is freed when EvictStaleJobs() removes completed jobs after 1 hour; the file is
    /// rehydrated on demand (tail-truncated to this same limit).
    /// </summary>
    internal const int MaxOutputLines = 10_000;
    private int _completionGuard;
    private readonly Subject<string> _outputSubject = new();
    private StreamWriter? _rawLogWriter;
    private StreamWriter? _eventWireWriter;
    private readonly object _rawLogLock = new();

    [JsonIgnore]
    public IObservable<string> OutputObservable => _outputSubject;

    public bool TryClaimCompletion() =>
        Interlocked.CompareExchange(ref _completionGuard, 1, 0) == 0;

    public string Id { get; init; } = "";
    public string Type { get; init; } = "";
    public string PlanFile { get; set; } = "";
    public string Project { get; init; } = "";
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int? DurationSeconds { get; set; }
    public JobArgsBase? TypedArgs { get; init; }
    public bool CancellationRequested { get; set; }

    /// <summary>
    /// True once this launch attempt has acquired a permit from the job-slot semaphore. Distinct
    /// from <c>Status == Running</c>: the pre-Running launch guard (repo-ownership check, #1340)
    /// can run — and release the slot via <c>FailJobAndReleaseSlot</c> — before <c>Status</c> ever
    /// flips to <see cref="JobStatus.Running"/>, so a concurrent <c>StopJob</c> keyed off
    /// <c>Status == Running</c> would wrongly skip releasing a slot it doesn't realize is held.
    /// </summary>
    public bool SlotReserved { get; set; }

    /// <summary>
    /// Plan state captured at job start, before the start transition. On Stop/Delete/
    /// Failed the plan is reverted to this "came-from" state. In-memory only (not
    /// persisted); after an app restart the fallback mapping in
    /// <see cref="Services.Jobs.JobCompletionHandler"/> is used instead.
    /// </summary>
    [JsonIgnore]
    public PlanStatus? PreviousPlanState { get; set; }

    public string? SessionId { get; set; }
    // Settable: the standalone CLI runners resolve the agent after the job object exists.
    public string Provider { get; set; } = "claude";
    public string? Model { get; set; }
    public int Priority { get; init; }
    public List<string>? WaitForJobIds { get; init; }
    public decimal? Cost { get; set; }
    public int? Tokens { get; set; }

    // Soft-cleared from the Jobs app; still shown in plan Details history
    public bool Cleared { get; set; }

    [JsonIgnore]
    public IEventParser? EventParser { get; set; }
    private IEventSerializer? _eventSerializer;

    // Process handle for non-interactive execution
    public Process? Process { get; set; }
    public int? ProcessId { get; set; }
    public string? StatusMessage { get; set; }
    public ConcurrentQueue<string> OutputLines { get; set; } = new();

    /// <summary>
    /// Guards against re-reading the EventWire file from disk on every GetJob call
    /// once this job's output has been hydrated (or confirmed to have none).
    /// </summary>
    [JsonIgnore] public bool OutputHydrated { get; set; }

    /// <summary>
    /// True for a job restored from the database after a master restart: its agent process is
    /// still alive but this process holds no Process handle or output stream for it.
    /// </summary>
    [JsonIgnore] public bool Detached { get; set; }

    public DateTime? LastOutputAt { get; set; }
    public CancellationTokenSource? TimeoutCts { get; set; }
    private volatile bool _staleOutputDetected;
    public bool StaleOutputDetected
    {
        get => _staleOutputDetected;
        set => _staleOutputDetected = value;
    }

    // Path to the .processing inbox file for CreatePlan job recovery
    public string? InboxFile { get; set; }

    // Pre-allocated plan ID for CreatePlan jobs (used for filesystem verification)
    public string? AllocatedPlanId { get; set; }

    // Reported by the agent via HTTP during execution
    public string? ReportedPlanId { get; set; }
    public string? ReportedPlanTitle { get; set; }

    /// <summary>
    /// Plan id this job relates to: the 5-digit prefix of <see cref="PlanFile"/>,
    /// falling back to the reported/allocated id (CreatePlan jobs hold a description
    /// in PlanFile, not a folder name). "" when the job is not tied to any plan.
    /// </summary>
    public string ResolvePlanId() =>
        Helpers.JobLogPaths.PlanIdFromFolderName(PlanFile)
        ?? ReportedPlanId
        ?? AllocatedPlanId
        ?? "";

    // Explicit failure reason declared by the promptware via `tendril job fail`.
    // When set, this wins over the output-scraping heuristic in SetCompletionStatus.
    public string? ReportedFailureReason { get; set; }

    // Agent launch details (persisted)
    public string? WorkingDirectory { get; set; }
    public string? CliCommand { get; set; }

    // Automatic logging metadata (transient, not persisted)
    [JsonIgnore] public string? CompiledPrompt { get; set; }
    [JsonIgnore] public int? ExitCode { get; set; }

    private string? _logFilePath;
    [JsonIgnore]
    public string? LogFilePath
    {
        get => _logFilePath;
        set
        {
            _logFilePath = value;
            OpenLogWriters();
        }
    }

    public void EnqueueOutput(string line)
    {
        LastOutputAt = DateTime.UtcNow;
        EventParser ??= new ClaudeEventParser();
        _eventSerializer ??= new JsonEventSerializer();
        AppendToRawLog(line);
        foreach (var evt in EventParser.ParseLine(line))
        {
            if (evt is SystemEvent or UnknownEvent) continue;

            if (evt is SessionInitEvent { Model: not null } initEvt)
                Model = initEvt.Model;

            var serialized = _eventSerializer.Serialize(evt);
            RecordEvent(serialized);
        }
    }

    public void EnqueueSystemOutput(string message)
    {
        LastOutputAt = DateTime.UtcNow;
        _eventSerializer ??= new JsonEventSerializer();
        AppendToRawLog(message);
        var evt = new TextEvent { Kind = AgentEventKind.Text, Text = message };
        RecordEvent(_eventSerializer.Serialize(evt));
    }

    public void FlushParser()
    {
        if (EventParser is null) return;
        _eventSerializer ??= new JsonEventSerializer();
        foreach (var evt in EventParser.Flush())
            RecordEvent(_eventSerializer.Serialize(evt));
    }

    /// <summary>
    /// Appends an event to the eventwire file (complete) and to the in-memory queue (tail-capped at
    /// <see cref="MaxOutputLines"/>), then publishes it to live subscribers.
    /// </summary>
    private void RecordEvent(string serialized)
    {
        AppendToEventWire(serialized);
        OutputLines.Enqueue(serialized);
        while (OutputLines.Count > MaxOutputLines)
            OutputLines.TryDequeue(out _);
        _outputSubject.OnNext(serialized);
    }

    /// <summary>
    /// Opens the raw and eventwire streams next to the Job Log. Both are appended to as output arrives, so a
    /// job killed by a crash still leaves a complete record on disk — nothing is deferred to completion.
    /// </summary>
    private void OpenLogWriters()
    {
        if (string.IsNullOrEmpty(_logFilePath)) return;
        _rawLogWriter = OpenAppender(Path.ChangeExtension(_logFilePath, ".raw.jsonl"));
        _eventWireWriter = OpenAppender(Path.ChangeExtension(_logFilePath, ".eventwire.jsonl"));
    }

    private static StreamWriter? OpenAppender(string path)
    {
        try
        {
            return new StreamWriter(path, append: true, System.Text.Encoding.UTF8) { AutoFlush = true };
        }
        catch
        {
            return null;
        }
    }

    private void AppendToRawLog(string line) => Append(_rawLogWriter, line);

    private void AppendToEventWire(string serializedEvent) => Append(_eventWireWriter, serializedEvent);

    private void Append(StreamWriter? writer, string line)
    {
        if (writer is null) return;
        lock (_rawLogLock)
        {
            try { writer.WriteLine(line); }
            catch { /* best-effort — don't crash the job */ }
        }
    }

    /// <summary>
    /// Closes the raw CLI transcript once the agent process has exited. Kept separate from
    /// <see cref="CloseLogWriters"/> because completion still enqueues Tendril's own events
    /// ("[Tendril] …", "[hook:…]") — those belong in the eventwire, but must not pollute the raw log,
    /// which is by definition the unparsed output of the agent CLI.
    /// </summary>
    internal void CloseRawLog()
    {
        lock (_rawLogLock)
        {
            try { _rawLogWriter?.Dispose(); } catch { }
            _rawLogWriter = null;
        }
    }

    internal void CloseLogWriters()
    {
        lock (_rawLogLock)
        {
            try { _rawLogWriter?.Dispose(); } catch { }
            try { _eventWireWriter?.Dispose(); } catch { }
            _rawLogWriter = null;
            _eventWireWriter = null;
        }
    }

    public void DisposeResources(ILogger? logger = null)
    {
        _outputSubject.OnCompleted();
        CloseLogWriters();

        // The prompt can be large and is already durable at {stem}.prompt.md. CliCommand is NOT freed here:
        // it is a persisted column, and clearing it before PersistJob would blank it in the database.
        CompiledPrompt = null;

        try
        {
            Process?.Dispose();
            logger?.LogDebug("Job {JobId}: Process disposed successfully", Id);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Job {JobId}: Failed to dispose Process", Id);
        }

        try
        {
            TimeoutCts?.Dispose();
            logger?.LogDebug("Job {JobId}: TimeoutCts disposed successfully", Id);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Job {JobId}: Failed to dispose TimeoutCts", Id);
        }

        Process = null;
        TimeoutCts = null;
    }
}

public record JobItemRow
{
    public string Id { get; init; } = "";
    /// <summary>
    /// Encoded animated-status string: "running:Running" while the job is running
    /// (so the Status column shimmers), "idle:Completed" / "idle:Failed" / etc.
    /// otherwise. Built via <see cref="Ivy.AnimatedStatusValue"/>.
    /// </summary>
    public string Status { get; init; } = "";
    public string PlanId { get; init; } = "";
    public string Plan { get; init; } = "";
    public string Type { get; init; } = "";
    public string Project { get; init; } = "";
    public string Timer { get; init; } = "";
    public string AgentOutput { get; init; } = "";
    public DateTime? LastOutputTimestamp { get; init; }
    public string Cost { get; init; } = "";
    public string Tokens { get; init; } = "";
    public string StatusMessage { get; init; } = "";
    public string? ErrorContext { get; init; }  // Multi-line error context for tooltip/expansion
}
